namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using static ApiDePuntaAPunta;

// Mensajes fallidos por HTTP (/api/mensajeria/fallidos): solo el administrador de la plataforma los ve,
// el historial guardado en la base, pedir un reprocesamiento una sola vez, y la cola de RabbitMQ que se
// mira sin consumirla.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class MensajesFallidosDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private const string Administrador = "admin@prueba.test";
    private static readonly string ColaFallidos = $"{TopologiaMensajeria.ColaWorker}.fallidos";

    // Son incidencias de toda la plataforma: ningún perfil de un operador o de un comercio entra
    [Theory]
    [InlineData(Operador1)]
    [InlineData(Despachador1)]
    [InlineData(Comercio1)]
    [InlineData(Repartidor1)]
    public async Task Solo_el_administrador_ve_y_maneja_los_mensajes_fallidos(string email)
    {
        var cliente = await api.IniciarSesionAsync(email);
        var id = Guid.NewGuid();

        var pedidos = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "api/mensajeria/fallidos"),
            new HttpRequestMessage(HttpMethod.Get, $"api/mensajeria/fallidos/{id}"),
            new HttpRequestMessage(HttpMethod.Get, "api/mensajeria/fallidos/resumen"),
            new HttpRequestMessage(HttpMethod.Post, "api/mensajeria/fallidos/inspeccionar"),
            new HttpRequestMessage(HttpMethod.Post, $"api/mensajeria/fallidos/{id}/reintentar")
        };

        foreach (var pedido in pedidos)
        {
            using var respuesta = await cliente.SendAsync(pedido);
            Assert.True(respuesta.StatusCode == HttpStatusCode.Forbidden, $"{pedido.Method} {pedido.RequestUri}: {respuesta.StatusCode}");
        }
    }

    [Fact]
    public async Task El_historial_se_filtra_por_estado_y_trae_el_detalle()
    {
        var administrador = await api.IniciarSesionAsync(Administrador);
        var pendiente = await FallidoAsync("Pendiente", "Timeout del receptor");
        var resuelto = await FallidoAsync("Resuelto", "Ya reprocesado");

        var pendientes = (await administrador.GetFromJsonAsync<PaginaResponse<MensajeFallidoResponse>>(
            "api/mensajeria/fallidos?estado=pendiente&tamanoPagina=100"))!;
        Assert.Contains(pendientes.Items, m => m.Id == pendiente);
        Assert.DoesNotContain(pendientes.Items, m => m.Id == resuelto);
        Assert.All(pendientes.Items, m => Assert.Equal("Pendiente", m.Estado));

        var detalle = (await administrador.GetFromJsonAsync<MensajeFallidoDetalleResponse>($"api/mensajeria/fallidos/{pendiente}"))!;
        Assert.Equal("Timeout del receptor", detalle.UltimoError);
        Assert.Equal("""{"prueba": true}""", detalle.Contenido);

        using (var noExiste = await administrador.GetAsync($"api/mensajeria/fallidos/{Guid.NewGuid()}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        }

        // Un estado que no existe se rechaza con el motivo, como el resto de la API
        using var estadoMalo = await administrador.GetAsync("api/mensajeria/fallidos?estado=Inventado");
        Assert.Equal(HttpStatusCode.BadRequest, estadoMalo.StatusCode);
        Assert.Contains("inválido", await MensajeAsync(estadoMalo));
    }

    [Fact]
    public async Task Un_reprocesamiento_se_pide_una_sola_vez_y_queda_quien_lo_pidio()
    {
        var administrador = await api.IniciarSesionAsync(Administrador);
        var id = await FallidoAsync("Pendiente", "Error de prueba");

        using (var primero = await administrador.PostAsync($"api/mensajeria/fallidos/{id}/reintentar", null))
        {
            Assert.True(primero.StatusCode == HttpStatusCode.Accepted,
                $"{primero.StatusCode}: {await primero.Content.ReadAsStringAsync()}");
        }

        using (var segundo = await administrador.PostAsync($"api/mensajeria/fallidos/{id}/reintentar", null))
        {
            Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        }

        // Uno que no existe tampoco
        using (var inexistente = await administrador.PostAsync($"api/mensajeria/fallidos/{Guid.NewGuid()}/reintentar", null))
        {
            Assert.Equal(HttpStatusCode.Conflict, inexistente.StatusCode);
        }

        // El publicador de reintentos puede haberlo tomado ya: lo que importa es que dejó de estar pendiente
        var detalle = (await administrador.GetFromJsonAsync<MensajeFallidoDetalleResponse>($"api/mensajeria/fallidos/{id}"))!;
        Assert.NotEqual("Pendiente", detalle.Estado);
        Assert.NotNull(detalle.SolicitadoPorId);
        Assert.NotNull(detalle.FechaSolicitudReproceso);
    }

    // Inspeccionar no saca los mensajes de la cola: después siguen estando todos
    [Fact]
    public async Task La_cola_de_fallidos_se_mira_sin_consumirla()
    {
        var administrador = await api.IniciarSesionAsync(Administrador);
        await api.PublicarEnColaAsync(ColaFallidos, ("""{"n":1}""", "Primer error"), ("""{"n":2}""", "Segundo error"));

        Assert.Equal(2, await PendientesEnColaAsync(administrador, esperados: 2));

        using (var limiteMalo = await administrador.PostAsync("api/mensajeria/fallidos/inspeccionar?limite=21", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, limiteMalo.StatusCode);
            Assert.Contains("entre 1 y 20", await MensajeAsync(limiteMalo));
        }

        using var inspeccion = await administrador.PostAsync("api/mensajeria/fallidos/inspeccionar?limite=1", null);
        Assert.Equal(HttpStatusCode.OK, inspeccion.StatusCode);
        var vistos = await inspeccion.Content.ReadFromJsonAsync<JsonElement>();
        var visto = Assert.Single(vistos.EnumerateArray());
        Assert.Equal("Primer error", visto.GetProperty("ultimoError").GetString());
        Assert.Equal("3", visto.GetProperty("intentos").GetString());

        Assert.Equal(2, await PendientesEnColaAsync(administrador, esperados: 2));
    }

    // RabbitMQ actualiza la cuenta con un poco de demora: espera hasta 5 segundos a ver la esperada
    private static async Task<int> PendientesEnColaAsync(HttpClient administrador, int esperados)
    {
        var cantidad = -1;

        for (var intento = 0; intento < 50 && cantidad != esperados; intento++)
        {
            var resumen = await administrador.GetFromJsonAsync<JsonElement>("api/mensajeria/fallidos/resumen");
            cantidad = resumen.GetProperty("cantidadPendientes").GetInt32();

            if (cantidad != esperados)
                await Task.Delay(100);
        }

        return cantidad;
    }

    private async Task<Guid> FallidoAsync(string estado, string error)
    {
        var id = Guid.NewGuid();

        await api.EjecutarAsync($"""
            INSERT INTO "MensajesFallidos"("Id", "MensajeId", "Tipo", "Contenido", "UltimoError", "Intentos", "FechaFallo", "Estado")
            VALUES ('{id}', '{Guid.NewGuid()}', '{TiposEvento.EnvioEstadoCambiado}', '{"{"}"prueba": true{"}"}', '{error}', 3, now(), '{estado}')
            """);

        return id;
    }

    private static async Task<string> MensajeAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()!;
}
