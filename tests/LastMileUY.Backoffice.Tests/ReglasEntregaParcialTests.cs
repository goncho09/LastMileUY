using System.Net;
using System.Text;
using LastMileUY.Backoffice.Pages.Operador;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Tests;

public class ReglasEntregaParcialTests
{
    private static readonly PublicarReglaEntregaParcialRequest Pedido = new()
    {
        PermiteEntregaParcial = false,
        DestinoBultosPendientes = "Devolucion",
        Motivo = "Los bultos que quedan vuelven al comercio"
    };

    [Fact]
    public async Task Publicar_lee_la_version_que_devuelve_la_api()
    {
        var api = CrearApi(HttpStatusCode.Created, """
            { "id": 9, "version": 3, "permiteEntregaParcial": false, "destinoBultosPendientes": "Devolucion",
              "motivo": "Los bultos que quedan vuelven al comercio", "publicadaPor": "Ana Silva",
              "fechaPublicacion": "2026-11-03T12:00:00Z" }
            """);

        var regla = await api.PublicarReglaEntregaParcialAsync(Pedido);

        Assert.Equal(3, regla.Version);
        Assert.False(regla.PermiteEntregaParcial);
        Assert.Equal("Ana Silva", regla.PublicadaPor);
    }

    [Fact]
    public async Task Publicar_muestra_el_motivo_si_la_api_la_rechaza()
    {
        var api = CrearApi(HttpStatusCode.BadRequest, """{ "mensaje": "Contá por qué se cambian las reglas." }""");

        var error = await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.PublicarReglaEntregaParcialAsync(Pedido));

        Assert.Equal("Contá por qué se cambian las reglas.", error.Message);
    }

    [Fact]
    public async Task Publicar_sin_permiso_se_rechaza()
    {
        var api = CrearApi(HttpStatusCode.Forbidden, "");

        await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.PublicarReglaEntregaParcialAsync(Pedido));
    }

    [Fact]
    public async Task Con_datos_de_prueba_cada_publicacion_es_una_version_nueva_y_queda_primera()
    {
        var api = new OperadorApiDePrueba();
        var antes = await api.ListarReglasEntregaParcialAsync();

        var publicada = await api.PublicarReglaEntregaParcialAsync(Pedido);
        var despues = await api.ListarReglasEntregaParcialAsync();

        Assert.Equal(antes[0].Version + 1, publicada.Version);
        Assert.Equal(publicada, despues[0]);
        Assert.Equal(antes.Count + 1, despues.Count);
    }

    [Theory]
    [InlineData("NuevaHojaDeRuta", "Vuelven a salir en otra hoja de ruta")]
    [InlineData("Devolucion", "Vuelven al comercio")]
    [InlineData("Otro", "Otro")]
    public void El_destino_de_los_bultos_se_muestra_en_palabras(string destino, string texto)
    {
        Assert.Equal(texto, IndexModel.TextoDestino(destino));
    }

    private static OperadorApiHttp CrearApi(HttpStatusCode codigo, string cuerpo) =>
        new(new HttpClient(new RespuestaFija(codigo, cuerpo)) { BaseAddress = new Uri("http://api.test/") });

    private class RespuestaFija(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            });
    }
}
