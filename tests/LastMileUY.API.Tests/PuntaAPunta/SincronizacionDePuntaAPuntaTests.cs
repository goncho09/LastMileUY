using LastMileUY.Contracts.Entregas;

namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using static ApiDePuntaAPunta;

// Sincronización por HTTP con los usuarios reales: el repartidor manda una entrega con su firma
// (la regla del operador 1 la pide), la reenvía sin duplicarla, y el despachador ve el resultado
// y la evidencia desde el Backoffice.
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

        var (rutaId, envioId) = await RutaDespachadaAsync(operador, despachador);

        // La app descarga los motivos de no entrega del operador (los crea el seeder)
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
        // Se cortó la conexión antes de que llegara la respuesta: la app la reenvía igual
        var reenvio = await SincronizarAsync(repartidor, entrega);

        Assert.Equal("Aplicada", primera.Resultado);
        Assert.False(primera.Duplicada);
        Assert.Equal("Aplicada", reenvio.Resultado);
        Assert.True(reenvio.Duplicada);

        // En el Backoffice: el envío entregado con un solo intento, y la operación con su firma
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

    private static async Task<ResultadoOperacionDto> SincronizarAsync(HttpClient repartidor, OperacionRequest operacion)
    {
        using var respuesta = await repartidor.PostAsJsonAsync("api/sincronizacion",
            new SincronizarRequest { Operaciones = [operacion] });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        return Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizarResponse>())!.Resultados);
    }

    // Un envío en depósito, en una ruta del repartidor 1 ya despachada (envío en tránsito).
    // La fecha es pasado mañana para no chocar con la ruta de HojasDeRutaDePuntaAPuntaTests.
    private async Task<(int RutaId, int EnvioId)> RutaDespachadaAsync(HttpClient operador, HttpClient despachador)
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);

        using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatario,
            DireccionId = direccion
        });
        var envioId = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;

        using (var deposito = await operador.PatchAsJsonAsync($"api/envios/{envioId}/estado",
                   new CambiarEstadoRequest { Estado = "EnDeposito" }))
        {
            Assert.True(deposito.IsSuccessStatusCode);
        }

        var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;

        using var ruta = await despachador.PostAsJsonAsync("api/rutas", new CrearRutaRequest
        {
            Nombre = "Ruta para sincronizar",
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
            RepartidorId = opciones.Repartidores[0].Id,
            VehiculoId = opciones.Vehiculos[0].Id,
            EnvioIds = [envioId]
        });
        Assert.Equal(HttpStatusCode.Created, ruta.StatusCode);
        var rutaId = (await ruta.Content.ReadFromJsonAsync<RutaDetalleDto>())!.Id;

        using (var despacho = await despachador.PostAsync($"api/rutas/{rutaId}/despachar", null))
        {
            Assert.Equal(HttpStatusCode.OK, despacho.StatusCode);
        }

        return (rutaId, envioId);
    }
}
