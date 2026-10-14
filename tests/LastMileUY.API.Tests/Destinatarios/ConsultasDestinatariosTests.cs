namespace LastMileUY.API.Tests.Destinatarios;

using LastMileUY.API.Modules.Destinatarios.Application;
using LastMileUY.API.Tests.Multitenancy;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Listado de destinatarios para el alta de envíos, contra la base real con RLS
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ConsultasDestinatariosTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_usuario_de_comercio_solo_ve_sus_destinatarios()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);

        var destinatarios = await new ConsultasDestinatariosService(db).ListarAsync(null);

        var unico = Assert.Single(destinatarios);
        Assert.Equal(DestinatarioComercio2, unico.Id);
    }

    [Fact]
    public async Task El_operador_ve_los_destinatarios_de_sus_comercios_y_no_los_de_otros()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var destinatarios = await new ConsultasDestinatariosService(db).ListarAsync(null);

        Assert.Contains(destinatarios, d => d.Id == 1);
        Assert.Contains(destinatarios, d => d.Id == DestinatarioComercio2);
        Assert.DoesNotContain(destinatarios, d => d.Id == DestinatarioComercio3);
    }

    [Fact]
    public async Task Cada_destinatario_trae_sus_direcciones_activas()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        var destinatarios = await new ConsultasDestinatariosService(db).ListarAsync(null);

        var ana = Assert.Single(destinatarios, d => d.Id == 1);
        var direccion = Assert.Single(ana.Direcciones);
        Assert.Equal("Rivera", direccion.Calle);
    }
}
