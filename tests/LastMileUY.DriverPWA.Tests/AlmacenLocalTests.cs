namespace LastMileUY.DriverPWA.Tests;

using LastMileUY.DriverPWA.Services;
using Microsoft.JSInterop;

// Las reglas de lo que la app guarda en el celular (docs/datos-locales.md): vencimiento y un solo
// usuario. El cifrado lo hace el navegador (wwwroot/js/almacen.js); acá el navegador se simula con
// un diccionario, para ver qué se le pide guardar y borrar.
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
        var (almacen, _, reloj) = Nuevo();

        await almacen.AsignarDuenoAsync("repartidor-1");
        reloj.Avanzar(TimeSpan.FromHours(23));
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        // Venció el dato del dueño, pero la hoja todavía no: entra otro repartidor
        reloj.Avanzar(TimeSpan.FromHours(2));
        await almacen.AsignarDuenoAsync("repartidor-2");

        Assert.Null(await almacen.LeerAsync<Hoja>("hoja:1"));
    }

    [Fact]
    public async Task Al_cerrar_sesion_se_borra_todo_aunque_la_api_no_responda()
    {
        var (almacen, navegador, _) = Nuevo();
        await almacen.GuardarAsync("hoja:1", new Hoja("Ana Silva"), TimeSpan.FromHours(8));

        var sinApi = new HttpClient(new SinConexion()) { BaseAddress = new Uri("https://api.prueba/") };
        var auth = new AuthService(sinApi, almacen);

        await Assert.ThrowsAsync<HttpRequestException>(auth.LogoutAsync);

        Assert.Empty(navegador.Datos);
    }

    private static (AlmacenLocal, NavegadorFalso, RelojFalso) Nuevo()
    {
        var navegador = new NavegadorFalso();
        var reloj = new RelojFalso();
        return (new AlmacenLocal(navegador, reloj), navegador, reloj);
    }

    // Hace de wwwroot/js/almacen.js: guarda los textos en memoria
    private class NavegadorFalso : IJSRuntime
    {
        public Dictionary<string, string> Datos { get; } = [];
        public int VecesBorradoTodo { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? resultado = null;

            switch (identifier)
            {
                case "lastMileAlmacen.guardar":
                    Datos[(string)args![0]!] = (string)args[1]!;
                    break;
                case "lastMileAlmacen.leer":
                    resultado = Datos.GetValueOrDefault((string)args![0]!);
                    break;
                case "lastMileAlmacen.borrar":
                    Datos.Remove((string)args![0]!);
                    break;
                case "lastMileAlmacen.borrarTodo":
                    Datos.Clear();
                    VecesBorradoTodo++;
                    break;
                default:
                    throw new InvalidOperationException($"Función inesperada: {identifier}");
            }

            return ValueTask.FromResult((TValue)resultado!);
        }
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
