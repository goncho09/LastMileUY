using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LastMileUY.Worker;

public sealed class ConsumidorCorreos(
    OpcionesConsumidor opciones,
    ProcesadorCorreos procesador,
    ILogger<ConsumidorCorreos> logger) : BackgroundService
{
    public const string ColaReintento = "lastmileuy.correos.reintento";
    public const string ColaFallidos = "lastmileuy.correos.fallidos";
    public const string ColaOcupados = "lastmileuy.correos.ocupados";

    private const int IntentosMaximos = 3;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(opciones.CadenaRabbitMq),
            AutomaticRecoveryEnabled = true
        };

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
            await using var canal = await conexion.CreateChannelAsync(cancellationToken: ct);

            await using var canalReintentos = await conexion.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                ct);

            await DeclararTopologiaAsync(canal, ct);

            await canal.BasicQosAsync(0, 1, global: false, cancellationToken: ct);

            var consumidor = new AsyncEventingBasicConsumer(canal);

            consumidor.ReceivedAsync += async (_, mensaje) =>
            {
                try
                {
                    var evento = JsonSerializer.Deserialize<EstadoEnvioCambiado>(mensaje.Body.Span);

                    if (evento is not null)
                        await procesador.ProcesarAsync(evento, ct);

                    await canal.BasicAckAsync(mensaje.DeliveryTag, false, ct);
                }
                catch (CorreoOcupadoException ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogInformation(ex, "Reserva ocupada: correo postergado 30 segundos");
                    await PublicarYConfirmarAsync(ColaOcupados, mensaje, incrementarIntentos: false);
                }
                catch (ConfirmacionCorreoInciertaException ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Confirmación incierta: revisar manualmente antes de reenviar");
                    await PublicarYConfirmarAsync(ColaFallidos, mensaje, incrementarIntentos: false);
                }
                catch (JsonException ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Mensaje de correo ilegible");
                    await PublicarYConfirmarAsync(ColaFallidos, mensaje, incrementarIntentos: false);
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
                        logger.LogError(errorCabecera, "Cabecera de intentos inválida; mensaje a revisión");
                        await PublicarYConfirmarAsync(ColaFallidos, mensaje, incrementarIntentos: false);
                        return;
                    }

                    var destino = intentos + 1 < IntentosMaximos ? ColaReintento : ColaFallidos;
                    await PublicarYConfirmarAsync(destino, mensaje, incrementarIntentos: true);
                }

                async Task PublicarYConfirmarAsync(string destino, BasicDeliverEventArgs entrega, bool incrementarIntentos)
                {
                    try
                    {
                        var headers = entrega.BasicProperties.Headers is null
                            ? new Dictionary<string, object?>()
                            : new Dictionary<string, object?>(entrega.BasicProperties.Headers);

                        if (incrementarIntentos)
                            headers["x-intentos-correo"] = LeerIntentos(headers) + 1;

                        var propiedades = new BasicProperties
                        {
                            DeliveryMode = DeliveryModes.Persistent,
                            MessageId = entrega.BasicProperties.MessageId,
                            ContentType = entrega.BasicProperties.ContentType,
                            Headers = headers
                        };

                        await canalReintentos.BasicPublishAsync(
                            exchange: "",
                            routingKey: destino,
                            mandatory: true,
                            basicProperties: propiedades,
                            body: entrega.Body,
                            cancellationToken: ct);

                        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken: ct);
                    }
                    catch (Exception error) when (!ct.IsCancellationRequested)
                    {
                        logger.LogError(error,
                            "Falló la publicación a {Destino}; se cierra el canal para recuperar el original", destino);
                        await canal.CloseAsync(cancellationToken: ct);
                        throw;
                    }
                }
            };

            await canal.BasicConsumeAsync(TopologiaMensajeria.ColaCorreos, autoAck: false, consumer: consumidor,
                cancellationToken: ct);

            logger.LogInformation("Consumidor de correos iniciado");

            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    public static async Task DeclararTopologiaAsync(IChannel canal, CancellationToken ct)
    {
        await canal.ExchangeDeclareAsync(TopologiaMensajeria.ExchangeEnvios, ExchangeType.Topic, durable: true,
            cancellationToken: ct);

        await canal.QueueDeclareAsync(TopologiaMensajeria.ColaCorreos, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: ct);

        foreach (var patron in TopologiaMensajeria.PatronesCorreos)
        {
            await canal.QueueBindAsync(TopologiaMensajeria.ColaCorreos, TopologiaMensajeria.ExchangeEnvios, patron,
                cancellationToken: ct);
        }

        await canal.QueueDeclareAsync(ColaReintento, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = 60000,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = TopologiaMensajeria.ColaCorreos
            },
            cancellationToken: ct);

        await canal.QueueDeclareAsync(ColaFallidos, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        await canal.QueueDeclareAsync(ColaOcupados, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = 30000,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = TopologiaMensajeria.ColaCorreos
            },
            cancellationToken: ct);
    }

    private static int LeerIntentos(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-intentos-correo", out var valor))
            return 0;

        return valor switch
        {
            int n when n >= 0 => n,
            long n when n >= 0 && n <= int.MaxValue => (int)n,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var n) && n >= 0 => n,
            _ => throw new InvalidOperationException("Encabezado x-intentos-correo inválido")
        };
    }
}
