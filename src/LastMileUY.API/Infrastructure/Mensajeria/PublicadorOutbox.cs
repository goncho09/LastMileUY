using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public class PublicadorOutbox(
    NpgsqlDataSource baseOutbox,
    ConexionRabbitMq rabbit,
    SenalOutbox senal,
    ILogger<PublicadorOutbox> logger) : BackgroundService
{
    private const int Lote = 50;

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

                await Task.Delay(espera, stoppingToken);
            }
        }
    }

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
                   "Estado", "EstadoAnterior", "Origen", "EsPublico", "CorrelacionId",
                   "CodigoBulto", "MotivoNoEntrega"
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
                CorrelacionId: lector.IsDBNull(11) ? null : lector.GetString(11),
                CodigoBulto: lector.IsDBNull(12) ? null : lector.GetString(12),
                MotivoNoEntrega: lector.IsDBNull(13) ? null : lector.GetString(13)));
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

        using var actividad = TelemetriaMensajeria.Actividades.StartActivity(
            "rabbitmq publish",
            ActivityKind.Producer);

        actividad?.SetTag("messaging.system", "rabbitmq");
        actividad?.SetTag("messaging.destination.name", TopologiaMensajeria.ExchangeEnvios);
        actividad?.SetTag("messaging.message.id", evento.EventoId.ToString());
        actividad?.SetTag("lastmileuy.envio.id", evento.EnvioId);

        var encabezados = new Dictionary<string, object?>();

        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(
                actividad?.Context ?? Activity.Current?.Context ?? default,
                Baggage.Current),
            encabezados,
            (headers, key, value) => headers[key] = Encoding.UTF8.GetBytes(value));

        var propiedades = new BasicProperties
        {
            MessageId = evento.EventoId.ToString(),
            CorrelationId = evento.CorrelacionId,
            Type = evento.Tipo,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(
                new DateTimeOffset(evento.OcurridoEn).ToUnixTimeSeconds()),
            Headers = encabezados
        };

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
