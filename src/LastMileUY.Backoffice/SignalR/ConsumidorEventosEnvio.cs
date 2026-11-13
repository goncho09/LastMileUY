using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LastMileUY.Backoffice.SignalR;

public class ConsumidorEventosEnvio(
    IConfiguration configuracion,
    IHubContext<EnviosHub> hub,
    ILogger<ConsumidorEventosEnvio> logger) : BackgroundService
{
    private const string ColaCompartidaAnterior = "lastmileuy.backoffice.envios";

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var cadena = configuracion.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:RabbitMQ");

        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(cadena),
            ClientProvidedName = "LastMileUY.Backoffice",
            AutomaticRecoveryEnabled = true
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conexion =
                    await fabrica.CreateConnectionAsync(stoppingToken);

                await using var canal =
                    await conexion.CreateChannelAsync(
                        cancellationToken: stoppingToken);

                await canal.ExchangeDeclareAsync(
                    TopologiaMensajeria.ExchangeEnvios,
                    ExchangeType.Topic,
                    durable: true,
                    cancellationToken: stoppingToken);

                await canal.QueueDeleteAsync(
                    ColaCompartidaAnterior,
                    cancellationToken: stoppingToken);

                var cola = await canal.QueueDeclareAsync(
                    queue: "",
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    cancellationToken: stoppingToken);

                await canal.QueueBindAsync(
                    cola.QueueName,
                    TopologiaMensajeria.ExchangeEnvios,
                    TopologiaMensajeria.PatronEventosEnvio,
                    cancellationToken: stoppingToken);

                await canal.BasicQosAsync(
                    0, 1, global: false, stoppingToken);

                var consumidor = new AsyncEventingBasicConsumer(canal);

                consumidor.ReceivedAsync += async (_, mensaje) =>
                {
                    try
                    {
                        var evento =
                            JsonSerializer.Deserialize<EstadoEnvioCambiado>(
                                mensaje.Body.Span);

                        if (evento is null ||
                            evento.Version > EstadoEnvioCambiado.VersionActual ||
                            evento.OperadorId <= 0)
                        {
                            logger.LogWarning(
                                "Evento de envío inválido o no compatible");

                            await canal.BasicRejectAsync(
                                mensaje.DeliveryTag,
                                requeue: false);

                            return;
                        }

                        await hub.Clients
                            .Group(EnviosHub.GrupoOperador(evento.OperadorId))
                            .SendAsync(
                                "EnvioActualizado",
                                evento,
                                stoppingToken);

                        await canal.BasicAckAsync(
                            mensaje.DeliveryTag,
                            multiple: false,
                            cancellationToken: stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Error notificando un evento de envío");

                        await canal.BasicRejectAsync(
                            mensaje.DeliveryTag,
                            requeue: false,
                            cancellationToken: stoppingToken);
                    }
                };

                await canal.BasicConsumeAsync(
                    cola.QueueName,
                    autoAck: false,
                    consumer: consumidor,
                    cancellationToken: stoppingToken);

                logger.LogInformation(
                    "SignalR escuchando eventos desde {Cola}",
                    cola.QueueName);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No se pudo mantener la conexión con RabbitMQ");

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
        }
    }
}
