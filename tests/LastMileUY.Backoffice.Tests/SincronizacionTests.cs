namespace LastMileUY.Backoffice.Tests;

using System.Net;
using System.Text;
using LastMileUY.Backoffice.Pages.Sincronizacion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using DetalleDeRuta = LastMileUY.Backoffice.Pages.Rutas.DetalleModel;

// Lo que llegó de la app del repartidor, en el Backoffice: el cliente HTTP, los datos de prueba,
// la página Sincronización, la sección del detalle de la ruta y la descarga de la evidencia
public class SincronizacionTests
{
    [Fact]
    public void La_url_del_historial_lleva_la_ruta_y_el_resultado()
    {
        var url = SincronizacionApiHttp.ArmarUrlListado(new FiltroOperaciones(3, "Conflicto", 2));

        Assert.Equal("api/sincronizacion/operaciones?pagina=2&tamanioPagina=20&rutaId=3&resultado=Conflicto", url);
    }

    [Fact]
    public async Task La_evidencia_se_lee_con_su_tipo_y_si_no_existe_devuelve_null()
    {
        var conImagen = new SincronizacionApiHttp(Http(HttpStatusCode.OK, [0x89, 0x50, 0x4E, 0x47], "image/png"));
        var sinImagen = new SincronizacionApiHttp(Http(HttpStatusCode.NotFound, [], "application/json"));

        var evidencia = await conImagen.ObtenerEvidenciaAsync(1, "firma");

        Assert.Equal("image/png", evidencia!.TipoContenido);
        Assert.Equal(4, evidencia.Contenido.Length);
        Assert.Null(await sinImagen.ObtenerEvidenciaAsync(1, "firma"));
    }

    [Fact]
    public async Task El_listado_lee_las_operaciones_de_la_api()
    {
        const string json = """
            {
              "items": [{
                "idOperacion": "6f1c2a4e-0000-4000-8000-000000000004", "tipo": "Entrega", "resultado": "Conflicto",
                "mensaje": "El envío ya está EnDeposito en el servidor.", "rutaId": 1, "envioId": 202,
                "codigoSeguimiento": "R4T8Y2U6P9HZ", "repartidor": "Lucía Méndez",
                "fechaHoraDispositivo": "2026-10-16T13:35:00Z", "fechaRecepcion": "2026-10-16T13:40:00Z",
                "intentoEntregaId": null, "tieneFirma": false, "tieneFoto": false
              }],
              "total": 1, "pagina": 1, "tamanioPagina": 20
            }
            """;
        var api = new SincronizacionApiHttp(Http(HttpStatusCode.OK, Encoding.UTF8.GetBytes(json), "application/json"));

        var operacion = Assert.Single((await api.ListarAsync(new FiltroOperaciones())).Items);

        Assert.Equal("Conflicto", operacion.Resultado);
        Assert.Null(operacion.IntentoEntregaId);
    }

    [Fact]
    public async Task La_pagina_filtra_por_resultado_para_ver_los_conflictos()
    {
        var pagina = new IndexModel(new SincronizacionApiDePrueba(), NullLogger<IndexModel>.Instance)
        {
            Resultado = "Conflicto"
        };

        await pagina.OnGetAsync(CancellationToken.None);

        var conflicto = Assert.Single(pagina.Operaciones.Items);
        Assert.Equal("Conflicto", conflicto.Resultado);
        Assert.Null(pagina.Error);
    }

    [Fact]
    public async Task Si_la_api_no_responde_la_pagina_muestra_un_mensaje()
    {
        var pagina = new IndexModel(new SincronizacionQueFalla(), NullLogger<IndexModel>.Instance);

        await pagina.OnGetAsync(CancellationToken.None);

        Assert.NotNull(pagina.Error);
    }

    [Fact]
    public async Task El_detalle_de_una_ruta_despachada_muestra_lo_que_llego_de_la_app()
    {
        var rutas = new RutasApiDePrueba();
        var ruta = await rutas.CrearAsync(new CrearRutaRequest
        {
            Nombre = "Centro",
            Fecha = DateOnly.FromDateTime(DateTime.Today).AddDays(1),
            RepartidorId = 1,
            VehiculoId = 2,
            EnvioIds = [201]
        });
        var pagina = Detalle(rutas);

        // Planificada: todavía no puede haber nada de la app, ni se consulta
        await pagina.OnGetAsync(ruta.Id, CancellationToken.None);
        Assert.Empty(pagina.Operaciones);

        await rutas.DespacharAsync(ruta.Id);
        await pagina.OnGetAsync(ruta.Id, CancellationToken.None);

        // Las 4 operaciones de prueba son de la ruta 1
        Assert.Equal(4, pagina.Operaciones.Count);
    }

    [Theory]
    [InlineData(1, "firma", true)]
    [InlineData(1, "foto", false)]
    [InlineData(1, "otra-cosa", false)]
    [InlineData(99, "firma", false)]
    public async Task La_evidencia_se_devuelve_como_imagen_o_404(int intento, string tipo, bool existe)
    {
        var pagina = new EvidenciaModel(new SincronizacionApiDePrueba());

        var resultado = await pagina.OnGetAsync(intento, tipo, CancellationToken.None);

        if (existe)
        {
            Assert.Equal("image/png", Assert.IsType<FileContentResult>(resultado).ContentType);
        }
        else
        {
            Assert.IsType<NotFoundResult>(resultado);
        }
    }

    [Fact]
    public void Cada_resultado_tiene_su_texto_y_su_color()
    {
        Assert.Equal("Conflicto", ResultadosSincronizacion.Texto("Conflicto"));
        Assert.Contains("amber", ResultadosSincronizacion.ClaseCss("Conflicto"));
        Assert.Equal("Intento fallido", ResultadosSincronizacion.TipoOperacion("IntentoFallido"));
        // Uno desconocido se muestra tal cual, en gris
        Assert.Equal("Otro", ResultadosSincronizacion.Texto("Otro"));
        Assert.Contains("slate", ResultadosSincronizacion.ClaseCss("Otro"));
    }

    private static DetalleDeRuta Detalle(IRutasApi rutas)
    {
        var http = new DefaultHttpContext();

        return new DetalleDeRuta(rutas, new SincronizacionApiDePrueba(), NullLogger<DetalleDeRuta>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataVacio())
        };
    }

    private static HttpClient Http(HttpStatusCode codigo, byte[] cuerpo, string tipo) =>
        new(new RespuestaFija(codigo, cuerpo, tipo)) { BaseAddress = new Uri("http://api.test/") };

    private class RespuestaFija(HttpStatusCode codigo, byte[] cuerpo, string tipo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var contenido = new ByteArrayContent(cuerpo);
            contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);

            return Task.FromResult(new HttpResponseMessage(codigo) { Content = contenido });
        }
    }

    private class SincronizacionQueFalla : ISincronizacionApi
    {
        public Task<PaginaDto<OperacionSincronizadaDto>> ListarAsync(FiltroOperaciones filtro, CancellationToken ct = default) =>
            throw new HttpRequestException("La API no responde");

        public Task<Evidencia?> ObtenerEvidenciaAsync(int intentoEntregaId, string tipo, CancellationToken ct = default) =>
            throw new HttpRequestException("La API no responde");
    }

    private class TempDataVacio : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
