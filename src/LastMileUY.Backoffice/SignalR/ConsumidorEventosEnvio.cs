
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

        // Reintentar la conexión si RabbitMQ todavía no está listo.
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

                // La cola compartida que se usaba antes: ya nadie la lee
                // y, si queda, acumula eventos para siempre.
                await canal.QueueDeleteAsync(
                    ColaCompartidaAnterior,
                    cancellationToken: stoppingToken);

                // Una cola propia de esta instancia, con nombre que elige
                // RabbitMQ, que se borra al cerrar la conexión:
                // - con varias instancias, cada una recibe todos los eventos
                //   (con una cola compartida se los repartirían);
                // - con el backoffice apagado no se acumulan eventos viejos
                //   que después llegarían como nuevos. Al reconectarse, la
                //   pantalla vuelve a consultar la API.
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

                            // Evita un bucle infinito con mensajes inválidos.
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

                        // Sin volverlo a la cola: si el error se repite,
                        // quedaría dando vueltas sin pausa. Perder un aviso
                        // no pierde datos: la pantalla lee el historial de
                        // la API y se pone al día con el siguiente.
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
