namespace LastMileUY.Backoffice.Tests;

using System.Net;
using System.Text;
using LastMileUY.Backoffice.Servicios;

public class EnviosApiHttpTests
{
    [Fact]
    public void La_url_del_listado_lleva_los_filtros_codificados()
    {
        var url = EnviosApiHttp.ArmarUrlListado(new FiltroEnvios("EnTransito", " Ana Silva ", 2, 20));

        Assert.Equal("api/envios?pagina=2&tamanioPagina=20&estado=EnTransito&buscar=Ana%20Silva", url);
    }

    [Fact]
    public void Sin_filtros_la_url_solo_lleva_la_pagina()
    {
        var url = EnviosApiHttp.ArmarUrlListado(new FiltroEnvios());

        Assert.Equal("api/envios?pagina=1&tamanioPagina=20", url);
    }

    [Fact]
    public async Task Listar_lee_la_respuesta_de_la_api()
    {
        const string json = """
            {
              "items": [{
                "id": 7, "codigoSeguimiento": "ABC123DEF456", "referenciaExterna": null,
                "comercio": "Tienda", "destinatario": "Ana Silva", "ciudad": "Montevideo",
                "departamento": "Montevideo", "estado": "EnDeposito", "modalidad": "Estandar",
                "cantidadBultos": 2, "fechaCreacion": "2026-10-10T12:00:00Z"
              }],
              "total": 1, "pagina": 1, "tamanioPagina": 20
            }
            """;
        var api = CrearApi(HttpStatusCode.OK, json);

        var resultado = await api.ListarAsync(new FiltroEnvios());

        var envio = Assert.Single(resultado.Items);
        Assert.Equal("ABC123DEF456", envio.CodigoSeguimiento);
        Assert.Equal(2, envio.CantidadBultos);
    }

    [Fact]
    public async Task Obtener_devuelve_null_si_la_api_responde_404()
    {
        var api = CrearApi(HttpStatusCode.NotFound, "");

        Assert.Null(await api.ObtenerAsync(1));
    }

    [Fact]
    public async Task Obtener_falla_si_la_api_responde_error()
    {
        var api = CrearApi(HttpStatusCode.InternalServerError, "");

        await Assert.ThrowsAsync<HttpRequestException>(() => api.ObtenerAsync(1));
    }

    private static EnviosApiHttp CrearApi(HttpStatusCode codigo, string cuerpo)
    {
        var http = new HttpClient(new RespuestaFija(codigo, cuerpo))
        {
            BaseAddress = new Uri("http://api.test/")
        };

        return new EnviosApiHttp(http);
    }

    // Simula la API: siempre responde lo mismo, sin red
    private class RespuestaFija(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            });
    }
}
