namespace LastMileUY.API.Tests.Multitenancy;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Operadores.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static BaseConDosOperadores;

// GET /api/operador: cada usuario ve solo la identidad y las reglas de su operador
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class OperadorActualTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Cada_operador_ve_su_identidad_y_sus_reglas()
    {
        var deA = await ObtenerAsync(OperadorA);
        var deB = await ObtenerAsync(OperadorB);

        Assert.NotNull(deA);
        Assert.NotNull(deB);
        Assert.Equal("Operador A", deA.Nombre);
        Assert.Equal("Operador B", deB.Nombre);
        Assert.Equal([ReglaDeA], deA.Reglas.Select(r => r.Nombre));
        Assert.Equal([ReglaDeB], deB.Reglas.Select(r => r.Nombre));
    }

    [Fact]
    public async Task Sin_operador_no_devuelve_nada()
    {
        Assert.Null(await ObtenerAsync(null));
    }

    [Fact]
    public async Task La_base_no_acepta_un_color_que_no_sea_hexadecimal()
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(
            $"UPDATE \"Operadores\" SET \"ColorPrimario\" = 'red; x' WHERE \"Id\" = {OperadorA}", conexion);

        var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());

        // 23514 = violación de un CHECK
        Assert.Equal("23514", error.SqlState);
    }

    private async Task<Contracts.Operadores.OperadorActualDto?> ObtenerAsync(int? operadorId)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        return await new OperadorActualService(db, new ContextoOperadorFijo(operadorId)).ObtenerAsync();
    }
}
