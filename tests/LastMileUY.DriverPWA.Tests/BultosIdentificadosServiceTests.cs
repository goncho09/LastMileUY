using LastMileUY.DriverPWA.Services;

namespace LastMileUY.DriverPWA.Tests;

public class BultosIdentificadosServiceTests
{
    [Fact]
    public async Task Lo_escaneado_sigue_ahi_al_volver_a_abrir_la_app()
    {
        var navegador = new NavegadorFalso();
        await Servicio(navegador).GuardarAsync(rutaId: 1, envioId: 7, [502, 501]);

        var guardados = await Servicio(navegador).LeerAsync(rutaId: 1);

        Assert.Equal([501, 502], guardados[7].Order());
    }

    [Fact]
    public async Task Cada_parada_guarda_lo_suyo_y_cada_hoja_de_ruta_por_separado()
    {
        var servicio = Servicio(new NavegadorFalso());

        await servicio.GuardarAsync(rutaId: 1, envioId: 7, [501]);
        await servicio.GuardarAsync(rutaId: 1, envioId: 8, [601]);
        await servicio.GuardarAsync(rutaId: 2, envioId: 7, [502]);

        var ruta1 = await servicio.LeerAsync(rutaId: 1);
        Assert.Equal([501], ruta1[7]);
        Assert.Equal([601], ruta1[8]);

        Assert.Equal([502], (await servicio.LeerAsync(rutaId: 2))[7]);
    }

    [Fact]
    public async Task Si_se_desmarcan_todos_la_parada_queda_sin_identificados()
    {
        var servicio = Servicio(new NavegadorFalso());

        await servicio.GuardarAsync(rutaId: 1, envioId: 7, [501]);
        await servicio.GuardarAsync(rutaId: 1, envioId: 7, []);

        Assert.False((await servicio.LeerAsync(rutaId: 1)).ContainsKey(7));
    }

    [Fact]
    public async Task Sin_nada_guardado_no_hay_identificados()
    {
        Assert.Empty(await Servicio(new NavegadorFalso()).LeerAsync(rutaId: 1));
    }

    private static BultosIdentificadosService Servicio(NavegadorFalso navegador) =>
        new(new AlmacenLocal(navegador, TimeProvider.System));
}
