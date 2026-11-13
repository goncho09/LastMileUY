
using Npgsql;

namespace LastMileUY.Worker;

public sealed class RepositorioCorreos(NpgsqlDataSource datos)
{
    private static async Task ConfigurarContextoAsync(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        int operadorId,
        int comercioId,
        CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand("""
            SELECT set_config('app.operador_id', @operador, true),
                   set_config('app.comercio_id', @comercio, true)
            """, conexion, transaccion);

        comando.Parameters.AddWithValue(
            "operador", operadorId.ToString());

        comando.Parameters.AddWithValue(
            "comercio", comercioId.ToString());

        await comando.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> ObtenerEstadoAsync(
        Guid eventoId,
        int operadorId,
        int comercioId,
        CancellationToken ct)
    {
        await using var conexion =
            await datos.OpenConnectionAsync(ct);

        await using var tx =
            await conexion.BeginTransactionAsync(ct);

        await ConfigurarContextoAsync(
            conexion, tx, operadorId, comercioId, ct);

        await using var comando = new NpgsqlCommand("""
            SELECT "Estado"
            FROM "CorreosSeguimiento"
            WHERE "EventoId" = @evento
              AND "OperadorId" = @operador
              AND "ComercioId" = @comercio
            """, conexion, tx);

        comando.Parameters.AddWithValue("evento", eventoId);
        comando.Parameters.AddWithValue("operador", operadorId);
        comando.Parameters.AddWithValue("comercio", comercioId);

        var resultado = await comando.ExecuteScalarAsync(ct);

        await tx.CommitAsync(ct);

        return resultado as string;
    }

    public async Task<Guid?> ReservarAsync(
        Guid eventoId,
        int envioId,
        int operadorId,
        int comercioId,
        CancellationToken ct)
    {
        var reservaId = Guid.NewGuid();

        await using var conexion =
            await datos.OpenConnectionAsync(ct);

        await using var tx =
            await conexion.BeginTransactionAsync(ct);

        await ConfigurarContextoAsync(
            conexion, tx, operadorId, comercioId, ct);

        await using var comando = new NpgsqlCommand("""
            INSERT INTO "CorreosSeguimiento"
                (
                    "EventoId",
                    "EnvioId",
                    "OperadorId",
                    "ComercioId",
                    "Estado",
                    "Intentos",
                    "ReservaId",
                    "ReservaHasta"
                )
            VALUES
                (
                    @evento,
                    @envio,
                    @operador,
                    @comercio,
                    'EnProceso',
                    1,
                    @reserva,
                    now() + interval '5 minutes'
                )
            ON CONFLICT ("EventoId") DO UPDATE
            SET
                "Estado" = 'EnProceso',
                "Intentos" =
                    "CorreosSeguimiento"."Intentos" + 1,
                "ReservaId" = EXCLUDED."ReservaId",
                "ReservaHasta" = EXCLUDED."ReservaHasta",
                "UltimoError" = NULL
            WHERE
                "CorreosSeguimiento"."OperadorId" = @operador
                AND "CorreosSeguimiento"."ComercioId" = @comercio
                AND "CorreosSeguimiento"."EnvioId" = @envio
                AND (
                    "CorreosSeguimiento"."Estado"
                        IN ('Pendiente', 'Fallido')
                    OR (
                        "CorreosSeguimiento"."Estado" = 'EnProceso'
                        AND (
                            "CorreosSeguimiento"."ReservaHasta" IS NULL
                            OR "CorreosSeguimiento"."ReservaHasta" < now()
                        )
                    )
                )
            RETURNING "ReservaId"
            """, conexion, tx);

        comando.Parameters.AddWithValue("evento", eventoId);
        comando.Parameters.AddWithValue("envio", envioId);
        comando.Parameters.AddWithValue("operador", operadorId);
        comando.Parameters.AddWithValue("comercio", comercioId);
        comando.Parameters.AddWithValue("reserva", reservaId);

        var resultado = await comando.ExecuteScalarAsync(ct);

        await tx.CommitAsync(ct);

        return resultado is Guid id ? id : null;
    }

    public async Task MarcarEnviadoAsync(
        Guid eventoId,
        Guid reservaId,
        int operadorId,
        int comercioId,
        CancellationToken ct)
    {
        await using var conexion =
            await datos.OpenConnectionAsync(ct);

        await using var tx =
            await conexion.BeginTransactionAsync(ct);

        await ConfigurarContextoAsync(
            conexion, tx, operadorId, comercioId, ct);

        await using var comando = new NpgsqlCommand("""
            UPDATE "CorreosSeguimiento"
            SET
                "Estado" = 'Enviado',
                "FechaEnvio" = now(),
                "UltimoError" = NULL,
                "ReservaId" = NULL,
                "ReservaHasta" = NULL
            WHERE "EventoId" = @evento
              AND "ReservaId" = @reserva
              AND "OperadorId" = @operador
              AND "ComercioId" = @comercio
              AND "Estado" = 'EnProceso'
            """, conexion, tx);

        comando.Parameters.AddWithValue("evento", eventoId);
        comando.Parameters.AddWithValue("reserva", reservaId);
        comando.Parameters.AddWithValue("operador", operadorId);
        comando.Parameters.AddWithValue("comercio", comercioId);

        var filas = await comando.ExecuteNonQueryAsync(ct);

        if (filas != 1)
        {
            throw new InvalidOperationException(
                "No se pudo confirmar el correo: reserva inválida o reemplazada.");
        }

        await tx.CommitAsync(ct);
    }

    public async Task MarcarFallidoAsync(
        Guid eventoId,
        Guid reservaId,
        int operadorId,
        int comercioId,
        string error,
        CancellationToken ct)
    {
        await using var conexion =
            await datos.OpenConnectionAsync(ct);

        await using var tx =
            await conexion.BeginTransactionAsync(ct);

        await ConfigurarContextoAsync(
            conexion, tx, operadorId, comercioId, ct);

        await using var comando = new NpgsqlCommand("""
            UPDATE "CorreosSeguimiento"
            SET
                "Estado" = 'Fallido',
                "UltimoError" = @error,
                "ReservaId" = NULL,
                "ReservaHasta" = NULL
            WHERE "EventoId" = @evento
              AND "ReservaId" = @reserva
              AND "OperadorId" = @operador
              AND "ComercioId" = @comercio
              AND "Estado" = 'EnProceso'
            """, conexion, tx);

        comando.Parameters.AddWithValue("evento", eventoId);
        comando.Parameters.AddWithValue("reserva", reservaId);
        comando.Parameters.AddWithValue("operador", operadorId);
        comando.Parameters.AddWithValue("comercio", comercioId);
        comando.Parameters.AddWithValue("error", error);

        var filas = await comando.ExecuteNonQueryAsync(ct);

        if (filas != 1)
        {
            throw new InvalidOperationException(
                "No se pudo registrar el fallo: reserva inválida o reemplazada.");
        }

        await tx.CommitAsync(ct);
    }
}
