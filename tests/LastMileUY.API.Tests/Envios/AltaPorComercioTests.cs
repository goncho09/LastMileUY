using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Envios;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

namespace LastMileUY.API.Tests.Envios;

// Alta de envíos desde el portal: el usuario de comercio trabaja con el operador
// que eligió al iniciar sesión y solo puede crear envíos para su comercio.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AltaPorComercioTests(BaseConDosOperadores baseDeDatos)
{
    private static ClaimsPrincipal CrearUsuarioComercio() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "usuario-comercio-1"),
            new Claim(ClaimTypes.Role, "Comercio")
        ], "Test"));

    [Fact]
    public async Task Un_usuario_de_comercio_con_operador_elegido_puede_crear_un_envio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        var servicio = new CrearEnvioService(
            db,
            new ContextoOperadorFijo(OperadorA, ComercioCompartido));

        var envio = await servicio.EjecutarAsync(
            new CrearEnvioRequest
            {
                ComercioId = ComercioCompartido,
                DestinatarioId = 1,
                DireccionId = 1,
                ReferenciaExterna = "PORTAL-COMERCIO-1",
                Bultos =
                [
                    new CrearBultoRequest
                    {
                        PesoKg = 1.5m,
                        LargoCm = 20,
                        AnchoCm = 15,
                        AltoCm = 10
                    }
                ]
            },
            CrearUsuarioComercio());

        Assert.Equal(ComercioCompartido, envio.ComercioId);
        Assert.Equal("Admitido", envio.Estado);
    }

    [Fact]
    public async Task Un_usuario_de_comercio_no_puede_crear_envios_para_otro_comercio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        var servicio = new CrearEnvioService(
            db,
            new ContextoOperadorFijo(OperadorA, ComercioCompartido));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            servicio.EjecutarAsync(
                new CrearEnvioRequest
                {
                    ComercioId = ComercioSoloDeA,
                    DestinatarioId = DestinatarioComercio2,
                    DireccionId = 1
                },
                CrearUsuarioComercio()));
    }
}