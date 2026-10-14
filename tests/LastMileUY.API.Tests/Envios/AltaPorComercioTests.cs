namespace LastMileUY.API.Tests.Envios;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Envios;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Alta de envíos desde el portal: el usuario de comercio trabaja con el operador que eligió
// al iniciar sesión (claim operador_id) además de su comercio (claim comercio_id).
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AltaPorComercioTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_usuario_de_comercio_con_operador_elegido_puede_crear_un_envio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var servicio = new CrearEnvioService(db, new ContextoOperadorFijo(OperadorA, ComercioCompartido));

        var envio = await servicio.EjecutarAsync(new CrearEnvioRequest
        {
            ComercioId = ComercioCompartido,
            DestinatarioId = 1,
            DireccionId = 1,
            ReferenciaExterna = "PORTAL-COMERCIO-1",
            Bultos = [new CrearBultoRequest { PesoKg = 1.5m, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
        });

        Assert.Equal(ComercioCompartido, envio.ComercioId);
        Assert.Equal("Admitido", envio.Estado);
    }

    [Fact]
    public async Task Un_usuario_de_comercio_no_puede_crear_envios_para_otro_comercio()
    {
        // Usuario del comercio 1 que intenta usar un destinatario del comercio 2
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var servicio = new CrearEnvioService(db, new ContextoOperadorFijo(OperadorA, ComercioCompartido));

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.EjecutarAsync(new CrearEnvioRequest
        {
            ComercioId = ComercioSoloDeA,
            DestinatarioId = DestinatarioComercio2,
            DireccionId = 1
        }));
    }
}
