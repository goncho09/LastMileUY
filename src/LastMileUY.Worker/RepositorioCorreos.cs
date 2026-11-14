
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

    public async Task MarcarSinDestinatarioAsync(
        Guid eventoId,
        Guid reservaId,
        int operadorId,
        int comercioId,
        string motivo,
        CancellationToken ct)
    {
        await using var conexion = await datos.OpenConnectionAsync(ct);
        await using var tx = await conexion.BeginTransactionAsync(ct);

        await ConfigurarContextoAsync(conexion, tx, operadorId, comercioId, ct);

        await using var comando = new NpgsqlCommand("""
            UPDATE "CorreosSeguimiento"
            SET
                "Estado" = 'SinDestinatario',
                "UltimoError" = @motivo,
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
        comando.Parameters.AddWithValue("motivo", motivo);

        if (await comando.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("No se pudo cerrar el correo: reserva inválida o reemplazada.");

        await tx.CommitAsync(ct);
    }

    public async Task<DestinatarioCorreo?> DestinatarioAsync(
        int envioId,
        int operadorId,
        int comercioId,
        CancellationToken ct)
    {
        await using var conexion = await datos.OpenConnectionAsync(ct);
        await using var tx = await conexion.BeginTransactionAsync(ct);

        await ConfigurarContextoAsync(conexion, tx, operadorId, comercioId, ct);

        await using var comando = new NpgsqlCommand("""
            SELECT d."Email", d."Nombre", d."Apellido", c."Nombre"
            FROM "Envios" e
            JOIN "Destinatarios" d ON d."Id" = e."DestinatarioId"
            JOIN "Comercios" c ON c."Id" = e."ComercioId"
            WHERE e."Id" = @envio
              AND e."OperadorId" = @operador
              AND e."ComercioId" = @comercio
              AND d."ComercioId" = @comercio
            """, conexion, tx);

        comando.Parameters.AddWithValue("envio", envioId);
        comando.Parameters.AddWithValue("operador", operadorId);
        comando.Parameters.AddWithValue("comercio", comercioId);

        DestinatarioCorreo? destinatario = null;

        await using (var lector = await comando.ExecuteReaderAsync(ct))
        {
            if (await lector.ReadAsync(ct))
            {
                destinatario = new DestinatarioCorreo(
                    lector.IsDBNull(0) ? null : lector.GetString(0),
                    lector.IsDBNull(1) ? "" : lector.GetString(1),
                    lector.IsDBNull(2) ? "" : lector.GetString(2),
                    lector.IsDBNull(3) ? "" : lector.GetString(3));
            }
        }

        await tx.CommitAsync(ct);
        return destinatario;
    }
}

public sealed record DestinatarioCorreo(string? Email, string Nombre, string Apellido, string Comercio);
