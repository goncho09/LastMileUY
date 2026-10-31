
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Worker;
using Npgsql;

namespace LastMileUY.API.Tests.Mensajeria;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class RepositorioMensajesFallidosTests(
    BaseConDosOperadores baseDeDatos)
{
    private async Task<Guid> CrearIncidenciaAsync()
    {
        var id = Guid.NewGuid();

        await using var conexion =
            new NpgsqlConnection(baseDeDatos.CadenaAdministrador);

        await conexion.OpenAsync();

        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO "MensajesFallidos"
            ("Id", "MensajeId", "Contenido", "Intentos",
             "FechaFallo", "Estado")
            VALUES (@id, @mensaje, '{}', 0, now(),
                    'ReprocesamientoSolicitado')
            """, conexion);

        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("mensaje", Guid.NewGuid().ToString());

        await comando.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<(string Estado, string? Error, int Intentos,
        DateTime? FechaResolucion)> ConsultarAsync(Guid id)
    {
        await using var conexion =
            new NpgsqlConnection(baseDeDatos.CadenaAdministrador);

        await conexion.OpenAsync();

        await using var comando = new NpgsqlCommand(
            """
            SELECT "Estado", "UltimoError",
                   "Intentos", "FechaResolucion"
            FROM "MensajesFallidos"
            WHERE "Id" = @id
            """, conexion);

        comando.Parameters.AddWithValue("id", id);

        await using var lector = await comando.ExecuteReaderAsync();

        Assert.True(await lector.ReadAsync());

        return (
            lector.GetString(0),
            lector.IsDBNull(1) ? null : lector.GetString(1),
            lector.GetInt32(2),
            lector.IsDBNull(3) ? null : lector.GetDateTime(3)
        );
    }

    [Fact]
    public async Task Reprocesamiento_exitoso_marca_resuelto()
    {
        var id = await CrearIncidenciaAsync();

        await using var dataSource =
            NpgsqlDataSource.Create(baseDeDatos.CadenaAdministrador);

        var repositorio = new RepositorioMensajesFallidos(dataSource);

        await repositorio.MarcarResueltoAsync(id, CancellationToken.None);

        var resultado = await ConsultarAsync(id);

        Assert.Equal("Resuelto", resultado.Estado);
        Assert.NotNull(resultado.FechaResolucion);
    }

    [Fact]
    public async Task Reprocesamiento_fallido_vuelve_a_pendiente()
    {
        var id = await CrearIncidenciaAsync();

        await using var dataSource =
            NpgsqlDataSource.Create(baseDeDatos.CadenaAdministrador);

        var repositorio = new RepositorioMensajesFallidos(dataSource);

        await repositorio.MarcarFallidoNuevamenteAsync(
            id,
            "Error de prueba",
            3,
            CancellationToken.None);

        var resultado = await ConsultarAsync(id);

        Assert.Equal("Pendiente", resultado.Estado);
        Assert.Equal("Error de prueba", resultado.Error);
        Assert.Equal(3, resultado.Intentos);
        Assert.Null(resultado.FechaResolucion);
    }
}
