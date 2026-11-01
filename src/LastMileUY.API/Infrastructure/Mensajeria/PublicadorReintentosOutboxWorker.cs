
using System.Text;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public sealed class PublicadorReintentosOutboxWorker(
    NpgsqlDataSource baseOutbox,
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

    
private async Task ProcesarPendientesAsync(CancellationToken ct)
{
    await using var conexion = await baseOutbox.OpenConnectionAsync(ct);
    await using var transaccion = await conexion.BeginTransactionAsync(ct);

    var pendientes = new List<(
        Guid Id,
        Guid MensajeFallidoId,
        string MensajeId,
        string Contenido,
        string? Tipo,
        string? CorrelacionId)>();

    await using (var comando = new NpgsqlCommand("""
        SELECT "Id", "MensajeFallidoId", "MensajeId",
               "Contenido", "Tipo", "CorrelacionId"
        FROM "MensajesReintentoOutbox"
        WHERE "FechaPublicacion" IS NULL
        ORDER BY "FechaCreacion"
        LIMIT 20
        FOR UPDATE SKIP LOCKED
        """, conexion, transaccion))
    {
        await using var lector = await comando.ExecuteReaderAsync(ct);

        while (await lector.ReadAsync(ct))
        {
            pendientes.Add((
                lector.GetGuid(0),
                lector.GetGuid(1),
                lector.GetString(2),
                lector.GetString(3),
                lector.IsDBNull(4) ? null : lector.GetString(4),
                lector.IsDBNull(5) ? null : lector.GetString(5)
            ));
        }
    }

    if (pendientes.Count == 0)
    {
        await transaccion.CommitAsync(ct);
        return;
    }

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

            await using var comando = new NpgsqlCommand("""
                UPDATE "MensajesReintentoOutbox"
                SET "FechaPublicacion" = now(),
                    "IntentosPublicacion" = "IntentosPublicacion" + 1,
                    "UltimoError" = NULL
                WHERE "Id" = @id
                """, conexion, transaccion);

            comando.Parameters.AddWithValue("id", mensaje.Id);
            await comando.ExecuteNonQueryAsync(ct);

            logger.LogInformation(
                "Reintento publicado: {Id}",
                mensaje.MensajeFallidoId);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await using var comando = new NpgsqlCommand("""
                UPDATE "MensajesReintentoOutbox"
                SET "IntentosPublicacion" = "IntentosPublicacion" + 1,
                    "UltimoError" = left(@error, 1000)
                WHERE "Id" = @id
                """, conexion, transaccion);

            comando.Parameters.AddWithValue("id", mensaje.Id);
            comando.Parameters.AddWithValue("error", ex.Message);

            await comando.ExecuteNonQueryAsync(ct);

            logger.LogError(
                ex,
                "Error publicando reintento {Id}",
                mensaje.MensajeFallidoId);

            // No seguimos publicando si falló un mensaje.
            break;
        }
    }

    await transaccion.CommitAsync(ct);
}

}
