using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LastMileUY.Worker;

public sealed class ConsumidorCorreos(
    OpcionesConsumidor opciones,
    NpgsqlDataSource baseDeDatos,
    ServicioCorreo servicioCorreo,
    RepositorioCorreos repositorioCorreos,
    IConfiguration configuracion,
    ILogger<ConsumidorCorreos> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(opciones.CadenaRabbitMq),
            AutomaticRecoveryEnabled = true
        };

        // Esperar a RabbitMQ si todavía no está disponible.
        IConnection conexion;
        while (true)
        {
            try
            {
                conexion = await fabrica.CreateConnectionAsync(ct);
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Esperando RabbitMQ para correos");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        await using (conexion)
        {
            await using var canal = await conexion.CreateChannelAsync(
                cancellationToken: ct);

            await using var canalReintentos =
                await conexion.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true),
                    ct);

            await canal.ExchangeDeclareAsync(
                TopologiaMensajeria.ExchangeEnvios,
                ExchangeType.Topic,
                durable: true,
                cancellationToken: ct);

            await canal.QueueDeclareAsync(
                TopologiaMensajeria.ColaCorreos,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);

            await canal.QueueBindAsync(
                TopologiaMensajeria.ColaCorreos,
                TopologiaMensajeria.ExchangeEnvios,
                TopologiaMensajeria.PatronEnvioAdmitido,
                cancellationToken: ct);


            const string colaReintento = "lastmileuy.correos.reintento";
            const string colaFallidos = "lastmileuy.correos.fallidos";
            const string colaOcupados = "lastmileuy.correos.ocupados";

            await canal.QueueDeclareAsync(
                colaReintento,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = 60000,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = TopologiaMensajeria.ColaCorreos
                },
                cancellationToken: ct);

            await canal.QueueDeclareAsync(
                colaFallidos,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);

            await canal.QueueDeclareAsync(
                colaOcupados,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = 30000,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] =
                        TopologiaMensajeria.ColaCorreos
                },
                cancellationToken: ct);

            await canal.BasicQosAsync(
                0, 1, global: false, cancellationToken: ct);

            var consumidor = new AsyncEventingBasicConsumer(canal);

            consumidor.ReceivedAsync += async (_, mensaje) =>
            {
                try
                {
                    var evento = JsonSerializer.Deserialize<EstadoEnvioCambiado>(
                        mensaje.Body.Span);

                    if (evento is null ||
                        evento.Tipo != TiposEvento.EnvioAdmitido)
                    {
                        await canal.BasicAckAsync(
                            mensaje.DeliveryTag, false, ct);
                        return;
                    }


                    var reservaId = await repositorioCorreos.ReservarAsync(
                        evento.EventoId,
                        evento.EnvioId,
                        evento.OperadorId,
                        evento.ComercioId,
                        ct);

                    if (reservaId is null)
                    {
                        var estado = await repositorioCorreos.ObtenerEstadoAsync(
                            evento.EventoId,
                            evento.OperadorId,
                            evento.ComercioId,
                            ct);

                        if (estado == "Enviado")
                        {
                            logger.LogInformation(
                                "Correo del evento {EventoId} ya enviado",
                                evento.EventoId);
                        }
                        else
                        {
                            // Otro proceso puede tener una reserva activa.
                            // No confirmar el mensaje original como completado.
                            throw new CorreoOcupadoException(
                                $"Correo reservado por otro proceso. Estado: {estado}");
                        }
                    }
                    else
                    {
                        try
                        {
                            await EnviarSeguimientoAsync(evento, ct);
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            // Fallo durante el envío: habilitar reintento.
                            // Un timeout SMTP puede ser ambiguo: revisar posibles duplicados.
                            try
                            {
                                await repositorioCorreos.MarcarFallidoAsync(
                                    evento.EventoId, reservaId.Value,
                                    evento.OperadorId, evento.ComercioId,
                                    ex.Message, ct);
                            }
                            catch (Exception errorRegistro)
                            {
                                logger.LogError(errorRegistro,
                                    "No se pudo registrar el fallo del correo {EventoId}",
                                    evento.EventoId);
                            }

                            throw;
                        }

                        // El SMTP ya retornó éxito. Si falla esta escritura,
                        // NO reintentar automáticamente el correo.
                        try
                        {
                            await repositorioCorreos.MarcarEnviadoAsync(
                                evento.EventoId, reservaId.Value,
                                evento.OperadorId, evento.ComercioId, ct);
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            throw new ConfirmacionCorreoInciertaException(
                                "Gmail aceptó el correo, pero no se pudo confirmar en PostgreSQL.", ex);
                        }
                    }

                    await canal.BasicAckAsync(
                        mensaje.DeliveryTag, false, ct);
                }

                catch (CorreoOcupadoException ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogInformation(ex,
                        "Reserva ocupada: correo postergado 30 segundos");
                    await PublicarYConfirmarAsync(
                        colaOcupados, mensaje, incrementarIntentos: false);
                }
                catch (ConfirmacionCorreoInciertaException ex)
                    when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex,
                        "Confirmación incierta: revisar manualmente antes de reenviar");
                    await PublicarYConfirmarAsync(
                        colaFallidos, mensaje, incrementarIntentos: false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error procesando correo de seguimiento");

                    int intentos;
                    try
                    {
                        intentos = LeerIntentos(mensaje.BasicProperties.Headers);
                    }
                    catch (Exception errorCabecera)
                    {
                        logger.LogError(errorCabecera,
                            "Cabecera de intentos inválida; mensaje a revisión");
                        await PublicarYConfirmarAsync(
                            colaFallidos, mensaje, incrementarIntentos: false);
                        return;
                    }

                    // Tres intentos totales: original + dos reintentos.
                    var destino = intentos + 1 < 3
                        ? colaReintento
                        : colaFallidos;

                    await PublicarYConfirmarAsync(
                        destino, mensaje, incrementarIntentos: true);
                }

                async Task PublicarYConfirmarAsync(
                    string destino,
                    BasicDeliverEventArgs entrega,
                    bool incrementarIntentos)
                {
                    try
                    {
                        var headers = entrega.BasicProperties.Headers is null
                            ? new Dictionary<string, object?>()
                            : new Dictionary<string, object?>(
                                entrega.BasicProperties.Headers);

                        if (incrementarIntentos)
                            headers["x-intentos-correo"] = LeerIntentos(headers) + 1;

                        var propiedades = new BasicProperties
                        {
                            DeliveryMode = DeliveryModes.Persistent,
                            MessageId = entrega.BasicProperties.MessageId,
                            ContentType = entrega.BasicProperties.ContentType,
                            Headers = headers
                        };

                        // Canal con publisher confirms: primero publicar, luego ACK.
                        await canalReintentos.BasicPublishAsync(
                            exchange: "",
                            routingKey: destino,
                            mandatory: true,
                            basicProperties: propiedades,
                            body: entrega.Body,
                            cancellationToken: ct);

                        await canal.BasicAckAsync(
                            entrega.DeliveryTag,
                            multiple: false,
                            cancellationToken: ct);
                    }
                    catch (Exception error) when (!ct.IsCancellationRequested)
                    {
                        logger.LogError(error,
                            "Falló la publicación a {Destino}; se cierra el canal para recuperar el original",
                            destino);
                        // No hacer NACK requeue:true: podría generar un bucle inmediato.
                        // Al cerrarse el canal, RabbitMQ reencola el mensaje sin ACK.
                        await canal.CloseAsync(cancellationToken: ct);
                        throw;
                    }
                }

            };

            await canal.BasicConsumeAsync(
                TopologiaMensajeria.ColaCorreos,
                autoAck: false,
                consumer: consumidor,
                cancellationToken: ct);

            logger.LogInformation("Consumidor de correos iniciado");

            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private static int LeerIntentos(IDictionary<string, object?>? headers)
    {
        if (headers is null ||
            !headers.TryGetValue("x-intentos-correo", out var valor))
            return 0;

        return valor switch
        {
            int n when n >= 0 => n,
            long n when n >= 0 && n <= int.MaxValue => (int)n,
            byte[] bytes when int.TryParse(
                Encoding.UTF8.GetString(bytes), out var n) && n >= 0 => n,
            _ => throw new InvalidOperationException(
                "Encabezado x-intentos-correo inválido")
        };
    }

    private sealed class ConfirmacionCorreoInciertaException(
        string mensaje, Exception inner) : Exception(mensaje, inner);

    private async Task EnviarSeguimientoAsync(
        EstadoEnvioCambiado evento,
        CancellationToken ct)
    {
        await using var conexion =
            await baseDeDatos.OpenConnectionAsync(ct);

        await using var transaccion =
            await conexion.BeginTransactionAsync(ct);

        await using (var contexto = new NpgsqlCommand("""
            SELECT set_config('app.operador_id', @operador, true),
                   set_config('app.comercio_id', @comercio, true)
            """, conexion, transaccion))
        {
            contexto.Parameters.AddWithValue(
                "operador", evento.OperadorId.ToString());
            contexto.Parameters.AddWithValue(
                "comercio", evento.ComercioId.ToString());

            await contexto.ExecuteNonQueryAsync(ct);
        }

        await using var comando = new NpgsqlCommand("""
            SELECT d."Email", d."Nombre", d."Apellido", c."Nombre"
            FROM "Envios" e
            JOIN "Destinatarios" d ON d."Id" = e."DestinatarioId"
            JOIN "Comercios" c ON c."Id" = e."ComercioId"
            WHERE e."Id" = @envio
              AND e."OperadorId" = @operador
              AND e."ComercioId" = @comercio
              AND d."ComercioId" = @comercio
            """, conexion, transaccion);

        comando.Parameters.AddWithValue("envio", evento.EnvioId);
        comando.Parameters.AddWithValue("operador", evento.OperadorId);
        comando.Parameters.AddWithValue("comercio", evento.ComercioId);

        string? email = null;
        string nombre = "";
        string apellido = "";
        string comercio = "";
        await using (var lector = await comando.ExecuteReaderAsync(ct))
        {
            if (await lector.ReadAsync(ct))
            {
                email = lector.IsDBNull(0) ? null : lector.GetString(0);
                nombre = lector.IsDBNull(1) ? "" : lector.GetString(1);
                apellido = lector.IsDBNull(2) ? "" : lector.GetString(2);
                comercio = lector.IsDBNull(3) ? "" : lector.GetString(3);
            }
        }

        await transaccion.CommitAsync(ct);

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogInformation(
                "Envío {EnvioId} sin email de destinatario",
                evento.EnvioId);
            return;
        }

        var urlBase = configuracion["Correo:UrlSeguimiento"]
            ?? throw new InvalidOperationException("Falta Correo:UrlSeguimiento");
        var enlace = $"{urlBase.TrimEnd('/')}/seguimiento/" +
                     Uri.EscapeDataString(evento.CodigoSeguimiento);
        if (!Uri.TryCreate(enlace, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Correo:UrlSeguimiento debe ser una URL HTTP(S) válida.");

        await servicioCorreo.EnviarSeguimientoAsync(
            new DatosCorreoSeguimiento(email, nombre, apellido, comercio,
                evento.CodigoSeguimiento, enlace), ct);

        logger.LogInformation(
            "Correo de seguimiento enviado para el envío {EnvioId}",
            evento.EnvioId);
    }
}

