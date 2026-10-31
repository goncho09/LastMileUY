using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LastMileUY.Worker;

// Recibe los eventos de envío que publica la API y se los pasa al procesador (ver docs/mensajeria.md).
// - Declara sus colas: así existen aunque el worker arranque antes que la API, y los eventos que se
//   publiquen mientras está apagado lo esperan ahí.
// - Confirma (ack) cada mensaje recién después de procesarlo: si el worker se cae a mitad, RabbitMQ
//   se lo vuelve a entregar (y el procesador descarta el repetido).
// - Si procesarlo falla, lo reintenta más tarde con espera creciente (5 s, 30 s, 2 min): lo deja en una
//   cola de espera, que se lo devuelve a la cola principal al vencer. Agotados los reintentos, o si el
//   mensaje no se puede leer, va a la cola de fallidos. Ningún mensaje se descarta.
public class ConsumidorEventosEnvio(
    OpcionesConsumidor opciones,
    IProcesadorEventos procesador,
    RepositorioMensajesFallidos repositorioFallidos,
    ILogger<ConsumidorEventosEnvio> logger) : BackgroundService
{
    // Encabezados que agrega el worker al reintentar
    public const string EncabezadoIntentos = "x-intentos";
    public const string EncabezadoError = "x-ultimo-error";

    // Cuántos mensajes puede tener sin confirmar a la vez
    private const ushort MensajesEnParalelo = 10;

    private static readonly TimeSpan EsperaMaximaConexion = TimeSpan.FromMinutes(1);

    private IConnection? conexion;
    private IChannel? canal;

    // Para mandar a reintento y a fallidos, con confirmación: el original se confirma recién cuando
    // RabbitMQ guardó la copia
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

        // Los mensajes llegan por el evento; esto solo mantiene vivo el servicio hasta que se apague
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // RabbitMQ puede no estar listo cuando arranca el worker: reintenta con espera creciente
    private async Task ConectarAsync(CancellationToken cancellationToken)
    {
        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(opciones.CadenaRabbitMq),
            ClientProvidedName = "LastMileUY.Worker",
            // Si se corta, el cliente reconecta solo y vuelve a suscribirse a la cola
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
        await canal.QueueBindAsync(
            TopologiaMensajeria.ColaWorker, TopologiaMensajeria.ExchangeEnvios,
            TopologiaMensajeria.PatronEventosEnvio, cancellationToken: cancellationToken);

        // Una cola de espera por reintento. Nadie las consume: el mensaje espera ahí lo que dice su
        // vencimiento y RabbitMQ lo devuelve a la cola principal (dead-letter). Una por nivel para que
        // un mensaje que espera 2 minutos no frene a uno que espera 5 segundos.
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

        // Los que no se pudieron procesar: quedan para revisarlos y reintentarlos a mano
        await canal.QueueDeclareAsync(
            ColaFallidos, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
    }

    public static string ColaReintento(int nivel) => $"{TopologiaMensajeria.ColaWorker}.reintento.{nivel}";

    public static string ColaFallidos => $"{TopologiaMensajeria.ColaWorker}.fallidos";

    private async Task RecibirAsync(BasicDeliverEventArgs mensaje, CancellationToken cancellationToken)
    {
        // El identificador del pedido HTTP que originó el evento: todo lo que se registre de acá en
        // adelante lo lleva, para seguir el cambio de punta a punta
        using var alcance = logger.BeginScope(
            "Correlación {CorrelacionId}, mensaje {MensajeId}",
            mensaje.BasicProperties.CorrelationId, mensaje.BasicProperties.MessageId);

        var intentosPrevios = LeerIntentos(mensaje.BasicProperties);

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
            // No se pudo dejar la copia (RabbitMQ con problemas): el original vuelve a la cola, no se pierde
            logger.LogError(ex, "No se pudo programar el reintento; el mensaje vuelve a la cola");
            await canal!.BasicNackAsync(mensaje.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }

    // Publica una copia del mensaje en otra cola (con los intentos y el último error) y confirma el original
    private async Task MoverAsync(
        BasicDeliverEventArgs mensaje, string cola, int intentos, string error, TimeSpan? espera,
        CancellationToken cancellationToken)
    {
        var original = mensaje.BasicProperties;
        var encabezados = new Dictionary<string, object?>
        {
            [EncabezadoIntentos] = intentos,
            [EncabezadoError] = error.Length > 500 ? error[..500] : error
        };

        var propiedades = new BasicProperties
        {
            MessageId = original.MessageId,
            CorrelationId = original.CorrelationId,
            Type = original.Type,
            ContentType = original.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Headers = encabezados,
            // Cuánto espera en la cola de reintento antes de volver a la principal
            Expiration = espera is null ? null : ((long)espera.Value.TotalMilliseconds).ToString()
        };
        
        if (cola == ColaFallidos)
        {
            await RegistrarFalloAsync(
                mensaje,
                intentos,
                error,
                cancellationToken);
        }

// Publicamos la copia antes de confirmar el mensaje original.
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

    }

    // null si no se puede leer o es de una versión más nueva que la que conoce el worker
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

// Configuración del consumidor (sección "Mensajeria" y la cadena "RabbitMQ")
public record OpcionesConsumidor(string CadenaRabbitMq, TimeSpan[] EsperasReintento)
{
    public static readonly TimeSpan[] EsperasPorDefecto =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];
}
