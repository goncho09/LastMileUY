using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Tests.Multitenancy;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AdministracionGlobalRlsTests(
    BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Administrador_global_puede_consultar_todos_los_operadores()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var operadores = await db.Operadores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(o => o.Id)
            .ToListAsync();

        Assert.Contains(BaseConDosOperadores.OperadorA, operadores);
        Assert.Contains(BaseConDosOperadores.OperadorB, operadores);
    }

    [Fact]
    public async Task Administrador_global_puede_consultar_todos_los_comercios()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var comercios = await db.Comercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Contains(
            BaseConDosOperadores.ComercioCompartido, comercios);

        Assert.Contains(
            BaseConDosOperadores.ComercioSoloDeA, comercios);

        Assert.Contains(
            BaseConDosOperadores.ComercioSoloDeB, comercios);
    }

    [Fact]
    public async Task Administrador_global_puede_consultar_los_vinculos()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var vinculos = await db.OperadoresComercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(v => new
            {
                v.OperadorId,
                v.ComercioId
            })
            .ToListAsync();

        Assert.Contains(vinculos, v =>
            v.OperadorId == BaseConDosOperadores.OperadorA &&
            v.ComercioId == BaseConDosOperadores.ComercioCompartido);

        Assert.Contains(vinculos, v =>
            v.OperadorId == BaseConDosOperadores.OperadorB &&
            v.ComercioId == BaseConDosOperadores.ComercioCompartido);
    }
}
