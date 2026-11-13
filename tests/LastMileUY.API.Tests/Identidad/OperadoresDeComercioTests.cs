using LastMileUY.API.Modules.Identidad.Application;
using LastMileUY.API.Tests.Multitenancy;

namespace LastMileUY.API.Tests.Identidad;

using static BaseConDosOperadores;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class OperadoresDeComercioTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_comercio_con_dos_operadores_puede_elegir_cualquiera_de_los_dos()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var operadores = await new OperadoresDeComercioService(db).ListarAsync(ComercioCompartido);

        Assert.Equal(new[] { OperadorA, OperadorB }, operadores.Select(o => o.Id).Order().ToArray());
    }

    [Fact]
    public async Task Un_comercio_solo_puede_elegir_los_operadores_con_los_que_trabaja()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var operadores = await new OperadoresDeComercioService(db).ListarAsync(ComercioSoloDeB);

        var unico = Assert.Single(operadores);
        Assert.Equal(OperadorB, unico.Id);
    }
}
