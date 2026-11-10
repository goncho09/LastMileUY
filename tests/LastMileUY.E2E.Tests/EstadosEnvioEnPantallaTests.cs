extern alias Api;
extern alias Portal;

namespace LastMileUY.E2E.Tests;

using LastMileUY.Backoffice.Servicios;
using EstadoEnvio = Api::LastMileUY.API.Modules.Envios.Domain.EstadoEnvio;
using PresentacionPortal = Portal::LastMileUY.CommercePortal.Servicios.PresentacionEnvios;

// Las pantallas tienen su propia lista de estados del envío (con el texto y el color). Si la API suma
// uno y una lista no, esa pantalla muestra el nombre interno y no deja filtrar por él (pasó con
// EntregadoParcialmente en el Backoffice y en el portal). Este proyecto ve los tres, por eso va acá
public class EstadosEnvioEnPantallaTests
{
    public static TheoryData<string> Estados() => new(Enum.GetNames<EstadoEnvio>());

    [Theory]
    [MemberData(nameof(Estados))]
    public void El_Backoffice_tiene_texto_para_cada_estado(string estado)
    {
        Assert.Contains(EstadosEnvio.Todos, e => e.Valor == estado);
    }

    [Theory]
    [MemberData(nameof(Estados))]
    public void El_portal_tiene_texto_para_cada_estado(string estado)
    {
        Assert.Contains(PresentacionPortal.Estados, e => e.Valor == estado);
    }

    [Fact]
    public void Las_listas_no_tienen_estados_que_la_API_no_conoce()
    {
        var api = Enum.GetNames<EstadoEnvio>();

        Assert.All(EstadosEnvio.Todos, e => Assert.Contains(e.Valor, api));
        Assert.All(PresentacionPortal.Estados, e => Assert.Contains(e.Valor, api));
    }
}
