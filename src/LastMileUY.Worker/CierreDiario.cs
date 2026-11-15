using Npgsql;

namespace LastMileUY.Worker;

public class CierreDiario(
    NpgsqlDataSource baseDeDatos,
    TimeProvider reloj,
    IConfiguration configuracion,
    ILogger<CierreDiario> logger) : BackgroundService
{
    private const long ClaveBloqueo = 0x4C4D_4349_4552_5245;

    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromMinutes(configuracion.GetValue("CierreDiario:MinutosEntreRecalculos", 15));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudieron recalcular los indicadores; se reintenta en el próximo ciclo");
            }

            await Task.Delay(intervalo, reloj, stoppingToken);
        }
    }

    public async Task<int> EjecutarAsync(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), HoraUruguay).DateTime);

        await using var conexion = await baseDeDatos.OpenConnectionAsync(ct);
        await using var tx = await conexion.BeginTransactionAsync(ct);

        await using (var bloqueo = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@clave)", conexion, tx))
        {
            bloqueo.Parameters.AddWithValue("clave", ClaveBloqueo);

            if (await bloqueo.ExecuteScalarAsync(ct) is not true)
            {
                logger.LogInformation("Otra instancia del worker está recalculando los indicadores");
                return 0;
            }
        }

        var operadores = new List<int>();

        await using (var comando = new NpgsqlCommand("""SELECT "Id" FROM "Operadores" WHERE "Activo" ORDER BY "Id" """, conexion, tx))
        await using (var lector = await comando.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                operadores.Add(lector.GetInt32(0));
        }

        var filas = 0;

        foreach (var operador in operadores)
        {
            await ConfigurarOperadorAsync(conexion, tx, operador, ct);

            filas += await CalcularAsync(conexion, tx, operador, hoy.AddDays(-1), cerrado: true, ct);
            filas += await CalcularAsync(conexion, tx, operador, hoy, cerrado: false, ct);
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("Indicadores recalculados: {Filas} días de {Operadores} operadores", filas, operadores.Count);
        return filas;
    }

    private static async Task ConfigurarOperadorAsync(NpgsqlConnection conexion, NpgsqlTransaction tx, int operador, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT set_config('app.operador_id', @operador, true), set_config('app.comercio_id', '', true)", conexion, tx);
        comando.Parameters.AddWithValue("operador", operador.ToString());
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> CalcularAsync(
        NpgsqlConnection conexion, NpgsqlTransaction tx, int operador, DateOnly dia, bool cerrado, CancellationToken ct)
    {
        var inicio = TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), HoraUruguay);
        var fin = TimeZoneInfo.ConvertTimeToUtc(dia.AddDays(1).ToDateTime(TimeOnly.MinValue), HoraUruguay);

        await using var comando = new NpgsqlCommand("""
            INSERT INTO "IndicadoresDiarios"
                ("OperadorId", "Dia", "EnviosCreados", "Entregados", "EntregadosATiempo", "NoEntregados",
                 "IntentosFallidos", "Importe", "Cerrado", "CalculadoEn")
            SELECT
                @operador,
                @dia,
                count(e."Id"),
                count(e."Id") FILTER (WHERE e."Estado" = 5),
                count(e."Id") FILTER (WHERE e."Estado" = 5 AND (
                    r."PlazoEntregaHoras" IS NULL OR r."PlazoEntregaHoras" <= 0
                    OR e."FechaEntrega" <= e."FechaCreacion" + make_interval(hours => r."PlazoEntregaHoras"))),
                count(e."Id") FILTER (WHERE e."Estado" IN (6, 8, 9, 10)),
                (SELECT count(*) FROM "IntentosEntrega" i
                    JOIN "Envios" e2 ON e2."Id" = i."EnvioId"
                    WHERE NOT i."Exitoso" AND e2."OperadorId" = @operador
                      AND i."FechaHora" >= @inicio AND i."FechaHora" < @fin),
                COALESCE(sum(e."CostoEnvio"), 0),
                @cerrado,
                now()
            FROM "Envios" e
            LEFT JOIN "ReglasOperativas" r ON r."Id" = e."ReglaOperativaId"
            WHERE e."OperadorId" = @operador AND e."FechaCreacion" >= @inicio AND e."FechaCreacion" < @fin
            ON CONFLICT ("OperadorId", "Dia") DO UPDATE SET
                "EnviosCreados" = EXCLUDED."EnviosCreados",
                "Entregados" = EXCLUDED."Entregados",
                "EntregadosATiempo" = EXCLUDED."EntregadosATiempo",
                "NoEntregados" = EXCLUDED."NoEntregados",
                "IntentosFallidos" = EXCLUDED."IntentosFallidos",
                "Importe" = EXCLUDED."Importe",
                "Cerrado" = EXCLUDED."Cerrado",
                "CalculadoEn" = EXCLUDED."CalculadoEn"
            """, conexion, tx);

        comando.Parameters.AddWithValue("operador", operador);
        comando.Parameters.AddWithValue("dia", dia);
        comando.Parameters.AddWithValue("inicio", inicio);
        comando.Parameters.AddWithValue("fin", fin);
        comando.Parameters.AddWithValue("cerrado", cerrado);

        return await comando.ExecuteNonQueryAsync(ct);
    }
}
