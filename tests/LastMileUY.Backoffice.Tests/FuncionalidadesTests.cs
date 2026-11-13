namespace LastMileUY.Backoffice.Tests;

using System.Net;
using System.Security.Claims;
using System.Text;
using LastMileUY.Backoffice.Pages.Rutas;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using IndexFuncionalidades = LastMileUY.Backoffice.Pages.Funcionalidades.IndexModel;

public class FuncionalidadesTests
{
    [Fact]
    public async Task La_pantalla_del_administrador_muestra_cada_operador_y_apaga_una_funcionalidad()
    {
        var api = new FuncionalidadesApiDePrueba();
        var pagina = Funcionalidades(api);

        await pagina.OnGetAsync(CancellationToken.None);
        Assert.Equal(2, pagina.Operadores.Count);
        Assert.All(pagina.Operadores, o => Assert.Equal(Contracts.Operadores.Funcionalidades.Todas.Count, o.Funcionalidades.Count));

        var resultado = await pagina.OnPostCambiarAsync(1, Contracts.Operadores.Funcionalidades.SeguimientoPublico, activa: false, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(resultado);
        Assert.Equal("\"Seguimiento público\" quedó apagada.", pagina.TempData["Aviso"]);

        var ahora = await api.ListarAsync();
        Assert.False(Activa(ahora, 1, Contracts.Operadores.Funcionalidades.SeguimientoPublico));
        Assert.True(Activa(ahora, 2, Contracts.Operadores.Funcionalidades.SeguimientoPublico));
        Assert.True(Activa(ahora, 1, Contracts.Operadores.Funcionalidades.OrdenSugerido));
    }

    [Fact]
    public async Task Si_la_api_rechaza_el_cambio_se_muestra_el_motivo()
    {
        var pagina = Funcionalidades(new FuncionalidadesApiDePrueba());

        var resultado = await pagina.OnPostCambiarAsync(99, Contracts.Operadores.Funcionalidades.OrdenSugerido, activa: false, CancellationToken.None);

        Assert.IsType<PageResult>(resultado);
        Assert.Equal("Ese operador no existe.", pagina.Error);
        Assert.Equal(2, pagina.Operadores.Count);
    }

    [Fact]
    public async Task Sin_orden_sugerido_la_pantalla_de_rutas_no_ofrece_ordenar()
    {
        var conOrden = Nueva(new OperadorFijo(OperadorApiDePrueba.Operador));
        var sinOrden = Nueva(new OperadorFijo(OperadorApiDePrueba.Operador with
        {
            Funcionalidades = [Contracts.Operadores.Funcionalidades.SeguimientoPublico]
        }));

        await conOrden.OnGetAsync(CancellationToken.None);
        await sinOrden.OnGetAsync(CancellationToken.None);

        Assert.True(conOrden.OrdenSugeridoHabilitado);
        Assert.False(sinOrden.OrdenSugeridoHabilitado);
    }

    [Fact]
    public async Task El_rechazo_por_funcionalidad_apagada_muestra_el_motivo_de_la_api()
    {
        const string motivo = "\"Orden sugerido de paradas\" no está habilitada para tu operador.";
        var api = new RutasApiHttp(new HttpClient(new RespuestaFija(HttpStatusCode.Forbidden, $$"""{"mensaje":"{{motivo.Replace("\"", "\\\"")}}"}"""))
        {
            BaseAddress = new Uri("http://api/")
        });

        var error = await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.SugerirOrdenAsync([1]));

        Assert.Equal(motivo, error.Message);
    }

    private static bool Activa(IReadOnlyList<FuncionalidadesOperadorDto> lista, int operadorId, string clave) =>
        lista.Single(o => o.OperadorId == operadorId).Funcionalidades.Single(f => f.Clave == clave).Activa;

    private static IndexFuncionalidades Funcionalidades(IFuncionalidadesApi api)
    {
        var http = new DefaultHttpContext { User = Usuario("Administrador") };

        return new IndexFuncionalidades(api, NullLogger<IndexFuncionalidades>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataEnMemoria())
        };
    }

    private static NuevaModel Nueva(IOperadorApi operador)
    {
        var http = new DefaultHttpContext { User = Usuario("Despachador") };

        return new NuevaModel(new RutasApiDePrueba(), NullLogger<NuevaModel>.Instance, operador)
        {
            PageContext = new PageContext { HttpContext = http }
        };
    }

    private static ClaimsPrincipal Usuario(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "prueba"));

    private sealed class OperadorFijo(OperadorActualDto operador) : IOperadorApi
    {
        public Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default) => Task.FromResult(operador);

        public Task<IReadOnlyList<ReglaEntregaParcialDto>> ListarReglasEntregaParcialAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReglaEntregaParcialDto> PublicarReglaEntregaParcialAsync(
            PublicarReglaEntregaParcialRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        public Task GuardarDepositoAsync(Coordenadas? deposito, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RespuestaFija(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
    }

    private sealed class TempDataEnMemoria : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
