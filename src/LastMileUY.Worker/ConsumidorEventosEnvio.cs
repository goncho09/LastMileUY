using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LastMileUY.Worker;

// Recibe los eventos de envío que publica la API (ver docs/mensajeria.md).
// Declara su cola y la une al exchange: así la cola existe aunque el worker arranque antes que la API,
// y los eventos que se publiquen mientras el worker está apagado lo esperan ahí.
// Confirma (ack) cada mensaje recién después de procesarlo: si el worker se cae a mitad, RabbitMQ
// se lo vuelve a entregar.
public class ConsumidorEventosEnvio(IConfiguration configuracion, ILogger<ConsumidorEventosEnvio> logger)
    : BackgroundService
{
    // Cuántos mensajes puede tener sin confirmar a la vez
    private const ushort MensajesEnParalelo = 10;

    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(1);

    private IConnection? conexion;
    private IChannel? canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cadena = configuracion.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:RabbitMQ.");

        await ConectarAsync(cadena, stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal!);
        consumidor.ReceivedAsync += (_, mensaje) => RecibirAsync(mensaje, stoppingToken);

        await canal!.BasicConsumeAsync(
            TopologiaMensajeria.ColaWorker, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);

        logger.LogInformation("Escuchando la cola {Cola}", TopologiaMensajeria.ColaWorker);

        // Los mensajes llegan por el evento; esto solo mantiene vivo el servicio hasta que se apague
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // RabbitMQ puede no estar listo cuando arranca el worker: reintenta con espera creciente
    private async Task ConectarAsync(string cadena, CancellationToken cancellationToken)
    {
        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(cadena),
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

                await canal.ExchangeDeclareAsync(
                    TopologiaMensajeria.ExchangeEnvios, ExchangeType.Topic, durable: true,
                    cancellationToken: cancellationToken);
                await canal.QueueDeclareAsync(
                    TopologiaMensajeria.ColaWorker, durable: true, exclusive: false, autoDelete: false,
                    cancellationToken: cancellationToken);
                await canal.QueueBindAsync(
                    TopologiaMensajeria.ColaWorker, TopologiaMensajeria.ExchangeEnvios,
                    TopologiaMensajeria.PatronEventosEnvio, cancellationToken: cancellationToken);
                await canal.BasicQosAsync(0, MensajesEnParalelo, global: false, cancellationToken);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var espera = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, intento), EsperaMaxima.TotalSeconds));
                logger.LogWarning(ex, "No se pudo conectar a RabbitMQ; se reintenta en {Espera}", espera);
                await Task.Delay(espera, cancellationToken);
            }
        }
    }

    private async Task RecibirAsync(BasicDeliverEventArgs mensaje, CancellationToken cancellationToken)
    {
        // El identificador del pedido HTTP que originó el evento: todo lo que se registre de acá en
        // adelante lo lleva, para seguir el cambio de punta a punta
        using var alcance = logger.BeginScope(
            "Correlación {CorrelacionId}, mensaje {MensajeId}",
            mensaje.BasicProperties.CorrelationId, mensaje.BasicProperties.MessageId);

        EstadoEnvioCambiado? evento;

        try
        {
            evento = JsonSerializer.Deserialize<EstadoEnvioCambiado>(mensaje.Body.Span);
        }
        catch (JsonException ex)
        {
            // No es un evento que el worker entienda: reintentarlo no lo arregla
            logger.LogError(ex, "Mensaje con formato inválido; se descarta");
            await canal!.BasicNackAsync(mensaje.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        logger.LogInformation(
            "Evento {Tipo} del envío {EnvioId} (operador {OperadorId}): {EstadoAnterior} -> {Estado}",
            evento!.Tipo, evento.EnvioId, evento.OperadorId, evento.EstadoAnterior, evento.Estado);

        await canal!.BasicAckAsync(mensaje.DeliveryTag, multiple: false, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (canal is not null)
            await canal.DisposeAsync();

        if (conexion is not null)
            await conexion.DisposeAsync();
    }
}
