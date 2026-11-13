using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LastMileUY.Worker;

public class ConsumidorEventosEnvio(
    OpcionesConsumidor opciones,
    IProcesadorEventos procesador,
    RepositorioMensajesFallidos repositorioFallidos,
    ILogger<ConsumidorEventosEnvio> logger) : BackgroundService
{
    public const string EncabezadoIntentos = "x-intentos";
    public const string EncabezadoError = "x-ultimo-error";
    private const string EncabezadoIncidenciaOriginal = "x-incidencia-original-id";

    private const ushort MensajesEnParalelo = 10;

    private static readonly TimeSpan EsperaMaximaConexion = TimeSpan.FromMinutes(1);

    private IConnection? conexion;
    private IChannel? canal;

    private IChannel? canalPublicacion;

    private async Task RegistrarFalloAsync(
        BasicDeliverEventArgs mensaje,
        int intentos,
        string error,
        CancellationToken ct)
    {
        var contenido = Encoding.UTF8.GetString(mensaje.Body.Span);

        await repositorioFallidos.RegistrarAsync(
            mensaje.BasicProperties.MessageId ?? string.Empty,
            mensaje.BasicProperties.CorrelationId,
            mensaje.BasicProperties.Type,
            contenido,
            error,
            intentos,
            ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConectarAsync(stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal!);
        consumidor.ReceivedAsync += (_, mensaje) => RecibirAsync(mensaje, stoppingToken);

        await canal!.BasicConsumeAsync(
            TopologiaMensajeria.ColaWorker, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);

        logger.LogInformation("Escuchando la cola {Cola}", TopologiaMensajeria.ColaWorker);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ConectarAsync(CancellationToken cancellationToken)
    {
        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(opciones.CadenaRabbitMq),
            ClientProvidedName = "LastMileUY.Worker",
            AutomaticRecoveryEnabled = true
        };

        for (var intento = 1; ; intento++)
        {
            try
            {
                conexion = await fabrica.CreateConnectionAsync(cancellationToken);
                canal = await conexion.CreateChannelAsync(cancellationToken: cancellationToken);
                canalPublicacion = await conexion.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    cancellationToken);

                await DeclararColasAsync(canal, cancellationToken);
                await canal.BasicQosAsync(0, MensajesEnParalelo, global: false, cancellationToken);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var espera = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, intento), EsperaMaximaConexion.TotalSeconds));
                logger.LogWarning(ex, "No se pudo conectar a RabbitMQ; se reintenta en {Espera}", espera);
                await Task.Delay(espera, cancellationToken);
            }
        }
    }

    private async Task DeclararColasAsync(IChannel canal, CancellationToken cancellationToken)
    {
        await canal.ExchangeDeclareAsync(
            TopologiaMensajeria.ExchangeEnvios, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(
            TopologiaMensajeria.ColaWorker, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(
            "lastmileuy.correos",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await canal.QueueBindAsync(
            TopologiaMensajeria.ColaWorker, TopologiaMensajeria.ExchangeEnvios,
            TopologiaMensajeria.PatronEventosEnvio, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(
            TopologiaMensajeria.ColaWorker, TopologiaMensajeria.ExchangeEnvios,
            TopologiaMensajeria.PatronEventosBulto, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(
            "lastmileuy.correos",
            TopologiaMensajeria.ExchangeEnvios,
            TopologiaMensajeria.PatronEnvioAdmitido,
            cancellationToken: cancellationToken);

        for (var nivel = 1; nivel <= opciones.EsperasReintento.Length; nivel++)
        {
            await canal.QueueDeclareAsync(
                ColaReintento(nivel), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = TopologiaMensajeria.ColaWorker
                },
                cancellationToken: cancellationToken);
        }

        await canal.QueueDeclareAsync(
            ColaFallidos, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
    }

    public static string ColaReintento(int nivel) => $"{TopologiaMensajeria.ColaWorker}.reintento.{nivel}";

    public static string ColaFallidos => $"{TopologiaMensajeria.ColaWorker}.fallidos";

    private async Task RecibirAsync(BasicDeliverEventArgs mensaje, CancellationToken cancellationToken)
    {
        var contexto = Propagators.DefaultTextMapPropagator.Extract(
            default,
            mensaje.BasicProperties.Headers,
            (headers, key) =>
            {
                if (headers is null || !headers.TryGetValue(key, out var valor))
                    return [];

                return valor switch
                {
                    byte[] bytes => [Encoding.UTF8.GetString(bytes)],
                    ReadOnlyMemory<byte> bytes => [Encoding.UTF8.GetString(bytes.Span)],
                    string texto => [texto],
                    _ => []
                };
            });

        using var actividad = TelemetriaMensajeria.Actividades.StartActivity(
            "rabbitmq process",
            ActivityKind.Consumer,
            contexto.ActivityContext);

        actividad?.SetTag("messaging.system", "rabbitmq");
        actividad?.SetTag("messaging.destination.name", TopologiaMensajeria.ColaWorker);
        actividad?.SetTag("messaging.message.id", mensaje.BasicProperties.MessageId);

        using var alcance = logger.BeginScope(
            "Correlación {CorrelacionId}, mensaje {MensajeId}",
            mensaje.BasicProperties.CorrelationId, mensaje.BasicProperties.MessageId);

        var intentosPrevios = LeerIntentos(mensaje.BasicProperties);
        var incidenciaOriginalId = ObtenerIncidenciaOriginal(mensaje);

        try
        {
            var evento = Leer(mensaje);


            if (evento is null)
            {
                logger.LogError(
                    "Mensaje con formato o versión que el worker no entiende");

                try
                {
                    await MoverAsync(
                        mensaje,
                        ColaFallidos,
                        intentosPrevios,
                        "Formato o versión desconocidos",
                        null,
                        cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(
                        ex,
                        "No se pudo registrar o publicar el mensaje fallido");

                    actividad?.SetStatus(ActivityStatusCode.Error, ex.Message);

                    await canal!.BasicNackAsync(
                        mensaje.DeliveryTag,
                        multiple: false,
                        requeue: true,
                        cancellationToken);
                }
                return;
            }

            var resultado = await procesador.ProcesarAsync(evento, cancellationToken);

            if (resultado.Repetido)
                logger.LogInformation("Evento {EventoId} ya procesado; se descarta el repetido", evento.EventoId);
            else
                logger.LogInformation(
                    "Evento {Tipo} del envío {EnvioId} (operador {OperadorId}): {EstadoAnterior} -> {Estado}, {Avisos} avisos",
                    evento.Tipo, evento.EnvioId, evento.OperadorId, evento.EstadoAnterior, evento.Estado,
                    resultado.AvisosRegistrados);

            if (incidenciaOriginalId.HasValue)
            {
                await repositorioFallidos.MarcarResueltoAsync(
                    incidenciaOriginalId.Value,
                    cancellationToken);
            }

            await canal!.BasicAckAsync(mensaje.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await ReintentarAsync(mensaje, intentosPrevios, ex, cancellationToken);
        }
    }

    private async Task ReintentarAsync(
        BasicDeliverEventArgs mensaje, int intentosPrevios, Exception error, CancellationToken cancellationToken)
    {
        var intento = intentosPrevios + 1;

        try
        {
            if (intento <= opciones.EsperasReintento.Length)
            {
                var espera = opciones.EsperasReintento[intento - 1];
                logger.LogWarning(error, "Falló el intento {Intento}; se reintenta en {Espera}", intento, espera);
                await MoverAsync(mensaje, ColaReintento(intento), intento, error.Message, espera, cancellationToken);
            }
            else
            {
                logger.LogError(error, "Falló el intento {Intento}; sin más reintentos, va a fallidos", intento);
                await MoverAsync(mensaje, ColaFallidos, intento, error.Message, null, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "No se pudo programar el reintento; el mensaje vuelve a la cola");
            await canal!.BasicNackAsync(mensaje.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }

    private async Task MoverAsync(
        BasicDeliverEventArgs mensaje, string cola, int intentos, string error, TimeSpan? espera,
        CancellationToken cancellationToken)
    {
        var original = mensaje.BasicProperties;


        var encabezados = original.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(original.Headers);

        encabezados[EncabezadoIntentos] = intentos;
        encabezados[EncabezadoError] =
            error.Length > 500 ? error[..500] : error;

        var propiedades = new BasicProperties
        {
            MessageId = original.MessageId,
            CorrelationId = original.CorrelationId,
            Type = original.Type,
            ContentType = original.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Headers = encabezados,
            Expiration = espera is null ? null : ((long)espera.Value.TotalMilliseconds).ToString()
        };

        var incidenciaOriginalId = ObtenerIncidenciaOriginal(mensaje);

        if (cola == ColaFallidos && !incidenciaOriginalId.HasValue)
        {
            await RegistrarFalloAsync(
                mensaje,
                intentos,
                error,
                cancellationToken);
        }

        await canalPublicacion!.BasicPublishAsync(
            "",
            cola,
            mandatory: true,
            basicProperties: propiedades,
            body: mensaje.Body,
            cancellationToken: cancellationToken);

        await canal!.BasicAckAsync(
            mensaje.DeliveryTag,
            multiple: false,
            cancellationToken);

        if (cola == ColaFallidos && incidenciaOriginalId.HasValue)
        {
            try
            {
                await repositorioFallidos.MarcarFallidoNuevamenteAsync(
                    incidenciaOriginalId.Value,
                    error,
                    intentos,
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "No se pudo actualizar la incidencia {IncidenciaId} después de publicar el mensaje fallido",
                    incidenciaOriginalId.Value);
            }
        }

    }

    private static Guid? ObtenerIncidenciaOriginal(
        BasicDeliverEventArgs mensaje)
    {
        var encabezados = mensaje.BasicProperties.Headers;

        if (encabezados is null ||
            !encabezados.TryGetValue(
                "x-incidencia-original-id", out var valor))
            return null;

        var texto = valor switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes =>
                Encoding.UTF8.GetString(bytes.Span),
            string cadena => cadena,
            _ => null
        };

        return Guid.TryParse(texto, out var id)
            ? id
            : null;
    }

    private static EstadoEnvioCambiado? Leer(BasicDeliverEventArgs mensaje)
    {
        try
        {
            var evento = JsonSerializer.Deserialize<EstadoEnvioCambiado>(mensaje.Body.Span);
            return evento is not null && evento.Version <= EstadoEnvioCambiado.VersionActual ? evento : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int LeerIntentos(IReadOnlyBasicProperties propiedades) =>
        (propiedades.Headers is { } encabezados && encabezados.TryGetValue(EncabezadoIntentos, out var intentos)
            ? intentos
            : null) switch
        {
            int valor => valor,
            long valor => (int)valor,
            byte[] texto when int.TryParse(Encoding.UTF8.GetString(texto), out var valor) => valor,
            _ => 0
        };

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (canalPublicacion is not null)
            await canalPublicacion.DisposeAsync();

        if (canal is not null)
            await canal.DisposeAsync();

        if (conexion is not null)
            await conexion.DisposeAsync();
    }
}

public record OpcionesConsumidor(string CadenaRabbitMq, TimeSpan[] EsperasReintento)
{
    public static readonly TimeSpan[] EsperasPorDefecto =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];
}
