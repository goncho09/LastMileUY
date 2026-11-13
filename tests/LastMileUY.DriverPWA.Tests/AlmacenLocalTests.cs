using System.Net;
using LastMileUY.DriverPWA.Services;

namespace LastMileUY.DriverPWA.Tests;

public class AlmacenLocalTests
{
    private record Hoja(string Destinatario);

    [Fact]
    public async Task Lo_guardado_se_lee_hasta_que_vence()
    {
        var (almacen, navegador, reloj) = Nuevo();

        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(2));
        reloj.Avanzar(TimeSpan.FromHours(1));

        Assert.Equal("Ana Silva", (await almacen.LeerAsync<Hoja>("hoja:1"))!.Destinatario);

        reloj.Avanzar(TimeSpan.FromHours(1));

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
        Assert.DoesNotContain("hoja:1", navegador.Datos.Keys);
    }

    [Fact]
    public async Task Nada_dura_mas_que_una_jornada()
    {
        var (almacen, _, reloj) = Nuevo();

        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromDays(30));
        reloj.Avanzar(AlmacenLocal.DuracionMaxima);

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
    }

    [Fact]
    public async Task Si_inicia_sesion_otro_repartidor_se_borra_lo_del_anterior()
    {
        var (almacen, navegador, _) = Nuevo();

        await almacen.AsignarDuenoAsync("repartidor-1");
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        await almacen.AsignarDuenoAsync("repartidor-2");

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
        Assert.Equal(2, navegador.VecesBorradoTodo);
    }

    [Fact]
    public async Task Si_vuelve_a_entrar_el_mismo_repartidor_se_conserva()
    {
        var (almacen, navegador, _) = Nuevo();

        await almacen.AsignarDuenoAsync("repartidor-1");
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        await almacen.AsignarDuenoAsync("repartidor-1");

        Assert.NotNull(await almacen.LeerAsync<Hoja>("hoja:1"));
        Assert.Equal(1, navegador.VecesBorradoTodo);
    }

    [Fact]
    public async Task Si_no_se_sabe_de_quien_es_lo_guardado_se_borra()
    {
        var (almacen, navegador, _) = Nuevo();

        await almacen.AsignarDuenoAsync("repartidor-1");
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        navegador.Datos.Remove("dueno");
        await almacen.AsignarDuenoAsync("repartidor-1");

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
    }

    [Fact]
    public async Task Lo_pendiente_no_vence_pero_se_borra_si_entra_otro_repartidor()
    {
        var (almacen, _, reloj) = Nuevo();

        await almacen.AsignarDuenoAsync("repartidor-1");
        await almacen.GuardarPendienteAsync("pendientes", new Hoja("Ana Silva"));
        reloj.Avanzar(TimeSpan.FromDays(3));

        Assert.NotNull(await almacen.LeerPendienteAsync<Hoja>("pendientes"));

        await almacen.AsignarDuenoAsync("repartidor-1");
        Assert.NotNull(await almacen.LeerPendienteAsync<Hoja>("pendientes"));

        await almacen.AsignarDuenoAsync("repartidor-2");
        Assert.Null(await almacen.LeerPendienteAsync<Hoja>("pendientes"));
    }

    [Fact]
    public async Task Si_el_dueno_quedo_con_el_formato_anterior_se_borra_y_se_puede_entrar()
    {
        var (almacen, navegador, _) = Nuevo();
        await almacen.GuardarAsync("dueno", "repartidor-1", TimeSpan.FromHours(8));
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        await almacen.AsignarDuenoAsync("repartidor-1");

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
        Assert.Equal(1, navegador.VecesBorradoTodo);
    }


    [Fact]
    public async Task Al_cerrar_sesion_no_se_borran_datos_si_la_api_no_responde()
    {
        var (almacen, navegador, _) = Nuevo();

        await almacen.GuardarAsync(
            "hoja:1",
            new Hoja("Ana Silva"),
            TimeSpan.FromHours(8));

        var sinApi = new HttpClient(new SinConexion())
        {
            BaseAddress = new Uri("https://api.prueba/")
        };

        var cola = new ColaOperacionesService(almacen);
        var auth = new AuthService(sinApi, almacen, cola);

        await Assert.ThrowsAsync<HttpRequestException>(auth.LogoutAsync);

        Assert.NotEmpty(navegador.Datos);

        var hoja = await almacen.LeerAsync<Hoja>("hoja:1");

        Assert.NotNull(hoja);
    }

    [Fact]
    public async Task Al_cerrar_sesion_con_la_sesion_ya_vencida_se_borran_los_datos()
    {
        var (almacen, navegador, _) = Nuevo();
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        var sesionVencida = new HttpClient(new Responde(HttpStatusCode.Unauthorized))
        {
            BaseAddress = new Uri("https://api.prueba/")
        };
        var auth = new AuthService(sesionVencida, almacen, new ColaOperacionesService(almacen));

        await auth.LogoutAsync();

        Assert.Empty(navegador.Datos);
    }


    private static (AlmacenLocal, NavegadorFalso, RelojFalso) Nuevo()
    {
        var navegador = new NavegadorFalso();
        var reloj = new RelojFalso();
        return (new AlmacenLocal(navegador, reloj), navegador, reloj);
    }

    private class RelojFalso : TimeProvider
    {
        private DateTimeOffset ahora = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => ahora;

        public void Avanzar(TimeSpan tiempo) => ahora += tiempo;
    }

    private class SinConexion : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Sin conexión");
    }

    private class Responde(HttpStatusCode codigo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo));
    }
}
