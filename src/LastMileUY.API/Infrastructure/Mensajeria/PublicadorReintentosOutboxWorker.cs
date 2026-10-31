
using System.Text;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public sealed class PublicadorReintentosOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ConexionRabbitMq rabbit,
    ILogger<PublicadorReintentosOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(5));

        do
        {
            try
            {
                await ProcesarPendientesAsync(stoppingToken);
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
                    "Error procesando el Outbox de reintentos");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcesarPendientesAsync(
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var pendientes = await db.MensajesReintentoOutbox
            .Where(x => x.FechaPublicacion == null)
            .OrderBy(x => x.FechaCreacion)
            .Take(20)
            .ToListAsync(ct);

        if (pendientes.Count == 0)
            return;

        await using var canal = await rabbit.CrearCanalAsync(ct);

        foreach (var mensaje in pendientes)
        {
            try
            {
                var propiedades = new BasicProperties
                {
                    MessageId = mensaje.MensajeId,
                    CorrelationId = mensaje.CorrelacionId,
                    Type = mensaje.Tipo,
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    Headers = new Dictionary<string, object?>
                    {
                        ["x-incidencia-original-id"] =
                            mensaje.MensajeFallidoId.ToString(),
                        ["x-intentos"] = 0
                    }
                };

                await canal.BasicPublishAsync(
                    exchange: "",
                    routingKey: TopologiaMensajeria.ColaWorker,
                    mandatory: true,
                    basicProperties: propiedades,
                    body: Encoding.UTF8.GetBytes(mensaje.Contenido),
                    cancellationToken: ct);

                mensaje.FechaPublicacion = DateTime.UtcNow;
                mensaje.IntentosPublicacion++;

                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Reintento publicado: {Id}",
                    mensaje.MensajeFallidoId);
            }
            catch (Exception ex)
                when (!ct.IsCancellationRequested)
            {
                mensaje.IntentosPublicacion++;
                mensaje.UltimoError = ex.Message;

                await db.SaveChangesAsync(ct);

                logger.LogError(
                    ex,
                    "Error publicando reintento {Id}",
                    mensaje.MensajeFallidoId);
            }
        }
    }
}
