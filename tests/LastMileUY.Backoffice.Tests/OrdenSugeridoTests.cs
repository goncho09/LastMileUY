namespace LastMileUY.Backoffice.Tests;

using System.Net;
using System.Security.Claims;
using System.Text;
using LastMileUY.Backoffice.Pages.Rutas;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using IndexOperador = LastMileUY.Backoffice.Pages.Operador.IndexModel;

// "Ordenar por recorrido" al armar una hoja de ruta, y el depósito en "Mi operador"
public class OrdenSugeridoTests
{
    private static readonly DateOnly Manana = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

    [Fact]
    public async Task Ordenar_carga_los_numeros_en_el_orden_sugerido_sin_armar_la_ruta()
    {
        var api = new RutasApiDePrueba(new OperadorApiDePrueba());
        var pagina = Nueva(api, [205, 202, 203, 201, 204]);

        var resultado = await pagina.OnPostOrdenarAsync(CancellationToken.None);

        Assert.IsType<PageResult>(resultado);
        Assert.Null(pagina.Error);
        var sugerencia = pagina.Sugerencia!;
        Assert.True(sugerencia.DesdeDeposito);

        // Las de la mañana (201 y 204) antes que las de la tarde; el 202 no tiene ubicación: al final
        Assert.Equal([201, 204], sugerencia.EnvioIds.Take(2).Order());
        Assert.Equal(202, sugerencia.EnvioIds[^1]);
        Assert.Equal(["R4T8Y2U6P9HZ"], sugerencia.SinUbicacion);

        // Los números de orden quedan cargados en ese orden, y la pantalla los muestra primero
        Assert.Equal(sugerencia.EnvioIds, pagina.Orden.OrderBy(o => o.Value).Select(o => o.Key));
        Assert.Equal(sugerencia.EnvioIds, pagina.EnviosEnPantalla.Take(5).Select(e => e.Id));

        // No se armó nada todavía
        Assert.Equal(0, (await api.ListarAsync(new FiltroRutas())).Total);

        // Con esos números, la ruta sale en ese orden (y respeta las franjas)
        var armar = await pagina.OnPostAsync(CancellationToken.None);
        Assert.Null(pagina.Error);
        var ruta = await api.ObtenerAsync((int)Assert.IsType<RedirectToPageResult>(armar).RouteValues!["id"]!);
        Assert.Equal(sugerencia.EnvioIds, ruta!.Paradas.Select(p => p.EnvioId));
    }

    [Fact]
    public async Task Ordenar_sin_elegir_envios_avisa()
    {
        var pagina = Nueva(new RutasApiDePrueba(new OperadorApiDePrueba()), []);

        await pagina.OnPostOrdenarAsync(CancellationToken.None);

        Assert.Equal("Elegí los envíos que querés ordenar.", pagina.Error);
        Assert.Null(pagina.Sugerencia);
    }

    [Fact]
    public async Task Sin_deposito_los_kilometros_cuentan_desde_la_primera_parada()
    {
        var operador = new OperadorApiDePrueba();
        await operador.GuardarDepositoAsync(null);

        var sugerencia = await new RutasApiDePrueba(operador).SugerirOrdenAsync([201, 204]);

        Assert.False(sugerencia.DesdeDeposito);
    }

    [Fact]
    public async Task El_cliente_http_lee_la_sugerencia()
    {
        const string json = """
            { "envioIds": [3, 1, 2], "kmActual": 12.4, "kmSugerido": 8.1, "sinUbicacion": ["ABC"], "desdeDeposito": true }
            """;

        var sugerencia = await RutasHttp(HttpStatusCode.OK, json).SugerirOrdenAsync([1, 2, 3]);

        Assert.Equal([3, 1, 2], sugerencia.EnvioIds);
        Assert.Equal(8.1, sugerencia.KmSugerido);
        Assert.Equal(["ABC"], sugerencia.SinUbicacion);
    }

    [Fact]
    public async Task El_cliente_http_muestra_el_motivo_si_la_api_rechaza_la_lista()
    {
        var api = RutasHttp(HttpStatusCode.BadRequest, """{ "mensaje": "El envío 9 no existe." }""");

        var error = await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.SugerirOrdenAsync([9]));

        Assert.Equal("El envío 9 no existe.", error.Message);
    }

    // --- Depósito en "Mi operador" ---

    [Theory]
    [InlineData("-34.9011", -34.9011)]
    [InlineData("-34,9011", -34.9011)]
    [InlineData(" -56.164512 ", -56.164512)]
    public void Los_grados_se_leen_con_punto_o_coma(string texto, double esperado)
    {
        Assert.True(IndexOperador.LeerGrados(texto, out var grados));
        Assert.Equal((decimal)esperado, grados);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("-34.9.1")]
    [InlineData("34°54'")]
    public void Lo_que_no_es_un_numero_no_se_acepta(string? texto)
    {
        Assert.False(IndexOperador.LeerGrados(texto, out _));
    }

    [Fact]
    public async Task Guardar_y_borrar_el_deposito()
    {
        var api = new OperadorApiDePrueba();
        var pagina = Operador(api);

        await pagina.OnPostGuardarDepositoAsync("-34,8712", "-56.1654", borrar: false, CancellationToken.None);
        Assert.Null(pagina.ErrorDeposito);
        Assert.Equal(new Coordenadas(-34.8712m, -56.1654m), (await api.ObtenerActualAsync()).Deposito);

        await pagina.OnPostGuardarDepositoAsync(null, null, borrar: true, CancellationToken.None);
        Assert.Null((await api.ObtenerActualAsync()).Deposito);
    }

    [Theory]
    [InlineData("-34.87", "")]
    [InlineData("-95", "-56")]
    public async Task Un_deposito_incompleto_o_fuera_de_rango_no_se_guarda(string latitud, string longitud)
    {
        var api = new OperadorApiDePrueba();
        var antes = (await api.ObtenerActualAsync()).Deposito;
        var pagina = Operador(api);

        await pagina.OnPostGuardarDepositoAsync(latitud, longitud, borrar: false, CancellationToken.None);

        Assert.NotNull(pagina.ErrorDeposito);
        Assert.Equal(antes, (await api.ObtenerActualAsync()).Deposito);
    }

    private static NuevaModel Nueva(IRutasApi api, List<int> seleccionados)
    {
        var http = new DefaultHttpContext { User = Usuario("Despachador") };

        return new NuevaModel(api, NullLogger<NuevaModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
            Nombre = "Centro",
            Fecha = Manana,
            RepartidorId = 1,
            VehiculoId = 2,
            Seleccionados = seleccionados
        };
    }

    private static IndexOperador Operador(IOperadorApi api)
    {
        var http = new DefaultHttpContext { User = Usuario("Operador") };

        return new IndexOperador(api, NullLogger<IndexOperador>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataEnMemoria())
        };
    }

    private static ClaimsPrincipal Usuario(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "prueba"));

    private static RutasApiHttp RutasHttp(HttpStatusCode codigo, string cuerpo) =>
        new(new HttpClient(new RespuestaFija(codigo, cuerpo)) { BaseAddress = new Uri("http://api.test/") });

    private class RespuestaFija(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            });
    }

    private class TempDataEnMemoria : ITempDataProvider
    {
        private Dictionary<string, object> datos = [];

        public IDictionary<string, object> LoadTempData(HttpContext context) => datos;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => datos = new(values);
    }
}
