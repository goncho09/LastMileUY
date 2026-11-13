using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class SincronizacionDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task El_repartidor_sincroniza_una_entrega_y_el_backoffice_ve_el_resultado_y_la_firma()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);

        var (rutaId, envioId) = await RutasDePuntaAPunta.RutaDespachadaAsync(api, operador, despachador, diasAdelante: 2);

        var motivos = (await repartidor.GetFromJsonAsync<List<MotivoNoEntregaDto>>("api/sincronizacion/motivos"))!;
        Assert.NotEmpty(motivos);

        var entrega = new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(),
            Tipo = "Entrega",
            RutaId = rutaId,
            EnvioId = envioId,
            FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-30),
            Latitud = -34.9m,
            Longitud = -56.16m,
            NombreReceptor = "Ana Silva",
            FirmaBase64 = Png
        };

        var primera = await SincronizarAsync(repartidor, entrega);
        var reenvio = await SincronizarAsync(repartidor, entrega);

        Assert.Equal("Aplicada", primera.Resultado);
        Assert.False(primera.Duplicada);
        Assert.Equal("Aplicada", reenvio.Resultado);
        Assert.True(reenvio.Duplicada);

        var envio = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!;
        Assert.Equal("Entregado", envio.Estado);
        Assert.Single(envio.Eventos, e => e.Tipo == "Entregado");

        var operaciones = (await despachador.GetFromJsonAsync<PaginaDto<OperacionSincronizadaDto>>(
            $"api/sincronizacion/operaciones?rutaId={rutaId}"))!;
        var operacion = Assert.Single(operaciones.Items);
        Assert.True(operacion.TieneFirma);

        using var firma = await despachador.GetAsync($"api/sincronizacion/evidencias/{operacion.IntentoEntregaId}/firma");
        Assert.Equal(HttpStatusCode.OK, firma.StatusCode);
        Assert.Equal("image/png", firma.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(Despachador1, "POST", "api/sincronizacion")]
    [InlineData(Comercio1, "POST", "api/sincronizacion")]
    [InlineData(Repartidor1, "GET", "api/sincronizacion/operaciones")]
    [InlineData(Repartidor1, "GET", "api/sincronizacion/evidencias/1/firma")]
    [InlineData(Comercio1, "GET", "api/sincronizacion/motivos")]
    public async Task Cada_perfil_usa_solo_lo_suyo(string email, string metodo, string url)
    {
        var cliente = await api.IniciarSesionAsync(email);

        using var pedido = new HttpRequestMessage(new HttpMethod(metodo), url);

        if (metodo == "POST")
        {
            pedido.Content = JsonContent.Create(new SincronizarRequest());
        }

        using var respuesta = await cliente.SendAsync(pedido);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    internal static async Task<ResultadoOperacionDto> SincronizarAsync(HttpClient repartidor, OperacionRequest operacion)
    {
        using var respuesta = await repartidor.PostAsJsonAsync("api/sincronizacion",
            new SincronizarRequest { Operaciones = [operacion] });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        return Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizarResponse>())!.Resultados);
    }
}
