using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace LastMileUY.Worker;

public sealed class RepositorioMensajesFallidos(
    NpgsqlDataSource dataSource)
{
    
    public static Guid GenerarIdIncidencia(
        string mensajeId,
        string contenido,
        int intentos)
    {
        var identificador = string.IsNullOrWhiteSpace(mensajeId)
            ? Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(contenido)))
            : mensajeId;

        var clave = $"{identificador}:{intentos}";

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(clave));

        return new Guid(hash.AsSpan(0, 16));
    }

    
    public async Task<Guid> RegistrarAsync(
        string mensajeId,
        string? correlacionId,
        string? tipo,
        string contenido,
        string error,
        int intentos,
        CancellationToken ct)
    {
        var id = GenerarIdIncidencia(mensajeId, contenido, intentos);

        await using var conexion =
            await dataSource.OpenConnectionAsync(ct);

        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO "MensajesFallidos"
            (
                "Id",
                "MensajeId",
                "CorrelacionId",
                "Tipo",
                "Contenido",
                "UltimoError",
                "Intentos",
                "FechaFallo",
                "Estado"
            )
            VALUES
            (
                @id,
                @mensajeId,
                @correlacionId,
                @tipo,
                @contenido,
                @error,
                @intentos,
                @fecha,
                @estado
            )
            ON CONFLICT ("Id") DO NOTHING
            """,
            conexion);

        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("mensajeId", mensajeId);
        comando.Parameters.AddWithValue(
            "correlacionId",
            (object?)correlacionId ?? DBNull.Value);
        comando.Parameters.AddWithValue(
            "tipo",
            (object?)tipo ?? DBNull.Value);
        comando.Parameters.AddWithValue("contenido", contenido);
        comando.Parameters.AddWithValue("error", error);
        comando.Parameters.AddWithValue("intentos", intentos);
        comando.Parameters.AddWithValue("fecha", DateTime.UtcNow);
        comando.Parameters.AddWithValue("estado", "Pendiente");

        await comando.ExecuteNonQueryAsync(ct);

        return id;
    }
    
    
    public async Task MarcarResueltoAsync(
        Guid incidenciaId,
        CancellationToken ct)
    {
        await using var conexion =
            await dataSource.OpenConnectionAsync(ct);

        await using var comando = new NpgsqlCommand(
            """
            UPDATE "MensajesFallidos"
            SET "Estado" = 'Resuelto',
                "FechaResolucion" = @fecha
            WHERE "Id" = @id
              AND "Estado" IN (
                  'ReprocesamientoSolicitado',
                  'Reprocesando'
              )
            """,
            conexion);

        comando.Parameters.AddWithValue("id", incidenciaId);
        comando.Parameters.AddWithValue("fecha", DateTime.UtcNow);

        await comando.ExecuteNonQueryAsync(ct);
    }
    
    
    public async Task MarcarFallidoNuevamenteAsync(
        Guid incidenciaId,
        string error,
        int intentos,
        CancellationToken ct)
    {
        await using var conexion =
            await dataSource.OpenConnectionAsync(ct);

        await using var comando = new NpgsqlCommand(
            """
            UPDATE "MensajesFallidos"
            SET "Estado" = 'Pendiente',
                "UltimoError" = @error,
                "Intentos" = @intentos,
                "FechaFallo" = @fecha,
                "FechaResolucion" = NULL
            WHERE "Id" = @id
              AND "Estado" IN (
                  'ReprocesamientoSolicitado',
                  'Reprocesando'
              )
            """,
            conexion);

        comando.Parameters.AddWithValue("id", incidenciaId);
        comando.Parameters.AddWithValue("error", error);
        comando.Parameters.AddWithValue("intentos", intentos);
        comando.Parameters.AddWithValue("fecha", DateTime.UtcNow);

        await comando.ExecuteNonQueryAsync(ct);
    }
}