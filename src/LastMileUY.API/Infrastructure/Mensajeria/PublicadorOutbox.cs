using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using Npgsql;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Publica en RabbitMQ los mensajes del outbox, en orden, y los marca como publicados.
// - Se conecta a la base con su propio usuario (lastmileuy_publicador), que solo puede leer y
//   marcar el outbox: la aplicación solo puede escribirlo (ver la migración Outbox).
// - Con varias instancias de la API, FOR UPDATE SKIP LOCKED reparte los mensajes: cada uno lo toma
//   una sola instancia.
// - Espera la confirmación de RabbitMQ antes de marcar el mensaje. Si se cae entre publicar y marcar,
//   el mensaje sale dos veces: el worker descarta el repetido por EventoId (entrega "al menos una vez").
// - Si RabbitMQ no está, reintenta con espera creciente (hasta un minuto) sin perder nada.
public class PublicadorOutbox(
    NpgsqlDataSource baseOutbox,
    ConexionRabbitMq rabbit,
    SenalOutbox senal,
    ILogger<PublicadorOutbox> logger) : BackgroundService
{
    private const int Lote = 50;

    // Cada cuánto revisa la tabla aunque nadie avise (mensajes de otra instancia, reintentos)
    private static readonly TimeSpan Revision = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(1);

    private IChannel? canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fallosSeguidos = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publicados = await PublicarLoteAsync(stoppingToken);
                fallosSeguidos = 0;

                // Lote lleno: puede haber más esperando
                if (publicados == Lote)
                    continue;

                await senal.EsperarAsync(Revision, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                fallosSeguidos++;
                var espera = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, fallosSeguidos), EsperaMaxima.TotalSeconds));
                logger.LogWarning(ex, "No se pudo publicar el outbox; se reintenta en {Espera}", espera);

                // Si la API se está apagando, esto corta con OperationCanceledException y el host lo da por terminado
                await Task.Delay(espera, stoppingToken);
            }
        }
    }

    // Toma hasta un lote de pendientes, los publica y los marca. Si uno falla, anota el error y corta
    // ahí, para no publicar los siguientes antes que él.
    private async Task<int> PublicarLoteAsync(CancellationToken cancellationToken)
    {
        await using var conexion = await baseOutbox.OpenConnectionAsync(cancellationToken);
        await using var transaccion = await conexion.BeginTransactionAsync(cancellationToken);

        var pendientes = await LeerPendientesAsync(conexion, transaccion, cancellationToken);
        var publicados = 0;

        foreach (var evento in pendientes)
        {
            try
            {
                await PublicarAsync(evento, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await EjecutarAsync(conexion, transaccion, """
                    UPDATE "MensajesOutbox" SET "Intentos" = "Intentos" + 1, "UltimoError" = left(@error, 1000)
                    WHERE "Id" = @id
                    """, evento.EventoId, ex.Message, cancellationToken);
                await transaccion.CommitAsync(cancellationToken);
                throw;
            }

            await EjecutarAsync(conexion, transaccion, """
                UPDATE "MensajesOutbox" SET "FechaPublicacion" = now(), "Intentos" = "Intentos" + 1, "UltimoError" = NULL
                WHERE "Id" = @id
                """, evento.EventoId, null, cancellationToken);
            publicados++;
        }

        await transaccion.CommitAsync(cancellationToken);
        return publicados;
    }

    private static async Task<List<EstadoEnvioCambiado>> LeerPendientesAsync(
        NpgsqlConnection conexion, NpgsqlTransaction transaccion, CancellationToken cancellationToken)
    {
        await using var comando = new NpgsqlCommand($"""
            SELECT "Id", "Tipo", "OcurridoEn", "OperadorId", "ComercioId", "EnvioId", "CodigoSeguimiento",
                   "Estado", "EstadoAnterior", "Origen", "EsPublico", "CorrelacionId"
            FROM "MensajesOutbox"
            WHERE "FechaPublicacion" IS NULL
            ORDER BY "FechaCreacion"
            LIMIT {Lote}
            FOR UPDATE SKIP LOCKED
            """, conexion, transaccion);

        await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
        var pendientes = new List<EstadoEnvioCambiado>();

        while (await lector.ReadAsync(cancellationToken))
        {
            pendientes.Add(new EstadoEnvioCambiado(
                EventoId: lector.GetGuid(0),
                Tipo: lector.GetString(1),
                Version: EstadoEnvioCambiado.VersionActual,
                OcurridoEn: DateTime.SpecifyKind(lector.GetDateTime(2), DateTimeKind.Utc),
                OperadorId: lector.GetInt32(3),
                ComercioId: lector.GetInt32(4),
                EnvioId: lector.GetInt32(5),
                CodigoSeguimiento: lector.GetString(6),
                Estado: lector.GetString(7),
                EstadoAnterior: lector.IsDBNull(8) ? null : lector.GetString(8),
                Origen: lector.GetString(9),
                EsPublico: lector.GetBoolean(10),
                CorrelacionId: lector.IsDBNull(11) ? null : lector.GetString(11)));
        }

        return pendientes;
    }

    private async Task PublicarAsync(EstadoEnvioCambiado evento, CancellationToken cancellationToken)
    {
        if (canal is not { IsOpen: true })
        {
            if (canal is not null)
                await canal.DisposeAsync();

            canal = await rabbit.CrearCanalAsync(cancellationToken);
        }

        var propiedades = new BasicProperties
        {
            MessageId = evento.EventoId.ToString(),
            CorrelationId = evento.CorrelacionId,
            Type = evento.Tipo,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(evento.OcurridoEn).ToUnixTimeSeconds())
        };

        // mandatory: si no hay ninguna cola para recibirlo, RabbitMQ lo devuelve y queda en el outbox
        // (si no, lo descartaría en silencio). Con la confirmación activada, falla acá.
        await canal.BasicPublishAsync(
            TopologiaMensajeria.ExchangeEnvios,
            evento.Tipo,
            mandatory: true,
            basicProperties: propiedades,
            body: JsonSerializer.SerializeToUtf8Bytes(evento),
            cancellationToken: cancellationToken);
    }

    private static async Task EjecutarAsync(
        NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, Guid id, string? error,
        CancellationToken cancellationToken)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        comando.Parameters.AddWithValue("id", id);

        if (error is not null)
            comando.Parameters.AddWithValue("error", error);

        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (canal is not null)
            await canal.DisposeAsync();
    }
}
