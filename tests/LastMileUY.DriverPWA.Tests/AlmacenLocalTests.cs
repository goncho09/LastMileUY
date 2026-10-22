namespace LastMileUY.DriverPWA.Tests;

using LastMileUY.DriverPWA.Services;

// Las reglas de lo que la app guarda en el celular (docs/datos-locales.md): vencimiento y un solo
// usuario. El cifrado lo hace el navegador (wwwroot/js/almacen.js); acá el navegador se simula con
// un diccionario (NavegadorFalso), para ver qué se le pide guardar y borrar.
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
        // Además de no devolverlo, lo borra
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
        // Uno en el primer inicio de sesión (todavía no había dueño) y otro al cambiar de repartidor
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
        // Solo el borrado del primer inicio de sesión, cuando todavía no había dueño
        Assert.Equal(1, navegador.VecesBorradoTodo);
    }

    [Fact]
    public async Task Si_no_se_sabe_de_quien_es_lo_guardado_se_borra()
    {
        var (almacen, navegador, _) = Nuevo();

        await almacen.AsignarDuenoAsync("repartidor-1");
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        // Se perdió el dato del dueño, pero la hoja sigue: aunque entre el mismo, no se puede saber
        navegador.Datos.Remove("dueno");
        await almacen.AsignarDuenoAsync("repartidor-1");

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
    }

    [Fact]
    public async Task Lo_pendiente_no_vence_pero_se_borra_si_entra_otro_repartidor()
    {
        // Una entrega sin enviar no se puede perder por el paso del tiempo: solo sale de la cola
        // con un resultado final de la API, al cerrar sesión o si cambia el repartidor
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
        // Antes el dueño se guardaba con vencimiento ({ valor, venceEn }): no se sabe de quién es
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

        // Si la API falla, los datos locales deben conservarse.
        Assert.NotEmpty(navegador.Datos);

        var hoja = await almacen.LeerAsync<Hoja>("hoja:1");

        Assert.NotNull(hoja);
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
}
