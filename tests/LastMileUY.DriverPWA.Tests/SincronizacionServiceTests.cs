namespace LastMileUY.DriverPWA.Tests;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;
using LastMileUY.DriverPWA.Services;

// Cómo la app manda entregas e intentos a la API de sincronización: cada operación se guarda primero
// en la cola del celular, va sola en su pedido y lleva el mismo IdOperacion en cada reenvío, para
// que la API no la aplique dos veces. Sale de la cola solo con un resultado final.
public class SincronizacionServiceTests
{
    [Fact]
    public async Task Manda_la_operacion_sola_a_la_api_de_sincronizacion()
    {
        var api = new ApiFalsa(_ => Resultado("Aplicada"));
        var (servicio, _) = Servicio(api);

        var estado = await servicio.GuardarOperacionAsync(Entrega(envioId: 7));

        Assert.False(estado.Pendiente);
        Assert.Equal("Aplicada", estado.Resultado!.Resultado);
        var pedido = Assert.Single(
            api.Pedidos.Where(p => p.Ruta == "/api/sincronizacion"));
        Assert.Equal("/api/sincronizacion", pedido.Ruta);
        var operacion = Assert.Single(pedido.Cuerpo!.Operaciones);
        Assert.Equal(7, operacion.EnvioId);
        Assert.NotEqual(Guid.Empty, operacion.IdOperacion);
    }

    [Fact]
    public async Task Sin_conexion_queda_pendiente_en_el_celular()
    {
        var (servicio, navegador) = Servicio(new ApiFalsa(_ => throw new HttpRequestException("Sin conexión")));

        var estado = await servicio.GuardarOperacionAsync(Entrega(envioId: 7));

        Assert.True(estado.Pendiente);
        Assert.Null(estado.Resultado);
        Assert.True(await servicio.TieneOperacionPendienteAsync(7));
        Assert.False(await servicio.TieneOperacionPendienteAsync(8));

        // Sigue ahí aunque se cierre la app: otra instancia, sobre lo mismo guardado, la encuentra
        var (despues, _) = Servicio(new ApiFalsa(_ => Resultado("Aplicada")), navegador);
        Assert.True(await despues.TieneOperacionPendienteAsync(7));
    }

    [Fact]
    public async Task El_reenvio_de_una_pendiente_lleva_el_mismo_id_y_la_saca_de_la_cola()
    {
        var primera = true;
        var api = new ApiFalsa(_ =>
        {
            if (!primera)
                return Resultado("Aplicada");

            primera = false;
            throw new HttpRequestException("Sin conexión");
        });
        var (servicio, _) = Servicio(api);

        await servicio.GuardarOperacionAsync(Entrega(envioId: 7));
        var id = api.Pedidos[0].Cuerpo!.Operaciones[0].IdOperacion;

        var estado = await servicio.ReintentarOperacionAsync(id);

        Assert.False(estado.Pendiente);
        Assert.Equal(id, api.Pedidos[1].Cuerpo!.Operaciones[0].IdOperacion);
        Assert.False(await servicio.TieneOperacionPendienteAsync(7));
    }

    [Fact]
    public async Task Si_la_api_pide_reintentar_sigue_pendiente()
    {
        var (servicio, _) = Servicio(new ApiFalsa(_ => Resultado("Error", reintentar: true)));

        var estado = await servicio.GuardarOperacionAsync(Entrega(envioId: 7));

        Assert.True(estado.Pendiente);
        Assert.True(await servicio.TieneOperacionPendienteAsync(7));
    }

    [Theory]
    [InlineData("Aplicada")]
    [InlineData("Rechazada")]
    [InlineData("Conflicto")]
    public async Task Con_un_resultado_final_sale_de_la_cola_y_lo_proximo_es_una_operacion_nueva(string resultado)
    {
        // Rechazada: el repartidor corrige (por ejemplo, agrega la firma) y la manda de nuevo.
        // Con el mismo id la API devolvería el rechazo anterior sin mirarla.
        var api = new ApiFalsa(_ => Resultado(resultado));
        var (servicio, _) = Servicio(api);

        await servicio.GuardarOperacionAsync(Entrega(envioId: 7));

        Assert.False(await servicio.TieneOperacionPendienteAsync(7));

        await servicio.GuardarOperacionAsync(Entrega(envioId: 7));

        var sincronizaciones = api.Pedidos
            .Where(p => p.Ruta == "/api/sincronizacion")
            .ToList();

        Assert.Equal(2, sincronizaciones.Count);

        Assert.NotEqual(
            sincronizaciones[0].Cuerpo!.Operaciones[0].IdOperacion,
            sincronizaciones[1].Cuerpo!.Operaciones[0].IdOperacion);
    }

    [Fact]
    public async Task Cada_envio_tiene_su_propia_operacion()
    {
        var api = new ApiFalsa(_ => throw new HttpRequestException("Sin conexión"));
        var (servicio, _) = Servicio(api);

        await servicio.GuardarOperacionAsync(Entrega(envioId: 7));
        await servicio.GuardarOperacionAsync(Entrega(envioId: 8));

        Assert.NotEqual(api.Pedidos[0].Cuerpo!.Operaciones[0].IdOperacion, api.Pedidos[1].Cuerpo!.Operaciones[0].IdOperacion);
    }

    [Fact]
    public async Task Con_la_sesion_vencida_avisa_para_volver_a_iniciar_y_no_la_pierde()
    {
        var (servicio, _) = Servicio(new ApiFalsa(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.GuardarOperacionAsync(Entrega(envioId: 7)));

        Assert.True(await servicio.TieneOperacionPendienteAsync(7));
    }

    [Fact]
    public async Task Dos_sincronizaciones_a_la_vez_no_mandan_dos_veces_la_misma_operacion()
    {
        var enLaApi = new ManualResetEventSlim();
        var seguir = new ManualResetEventSlim();
        var conConexion = false;
        var api = new ApiFalsa(_ =>
        {
            if (!conConexion)
                throw new HttpRequestException("Sin conexión");

            // La primera sincronización queda esperando la respuesta de la API
            enLaApi.Set();
            seguir.Wait();
            return Resultado("Aplicada");
        });
        var (servicio, _) = Servicio(api);
        await servicio.GuardarOperacionAsync(Entrega(envioId: 7));
        conConexion = true;

        // Vuelve la conexión: la dispara el layout y también la pantalla de operaciones
        var primera = Task.Run(servicio.SincronizarPendientesAsync);
        enLaApi.Wait(TimeSpan.FromSeconds(5));
        var segunda = await servicio.SincronizarPendientesAsync();
        seguir.Set();

        Assert.Equal((0, 1), segunda);
        Assert.Equal((1, 0), await primera);
        Assert.Equal(2, api.Pedidos.Count(p => p.Ruta == "/api/sincronizacion"));
    }

    [Fact]
    public async Task No_se_reintenta_una_operacion_que_ya_no_esta_pendiente()
    {
        var (servicio, _) = Servicio(new ApiFalsa(_ => Resultado("Aplicada")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReintentarOperacionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Los_motivos_salen_de_la_api_de_sincronizacion()
    {
        var api = new ApiFalsa(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new { id = 1, nombre = "Ausente", descripcion = (string?)null } })
        });

        var motivos = await Servicio(api).Servicio.ObtenerMotivosAsync();

        Assert.Equal("Ausente", Assert.Single(motivos).Nombre);
        Assert.Equal("/api/sincronizacion/motivos", Assert.Single(api.Pedidos).Ruta);
    }

    [Theory]
    [InlineData(null, "/api/sincronizacion/mias?pagina=2")]
    [InlineData("Conflicto", "/api/sincronizacion/mias?pagina=2&resultado=Conflicto")]
    public async Task Mis_operaciones_se_piden_filtradas_y_por_pagina(string? resultado, string url)
    {
        var api = new ApiFalsa(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PaginaDto<OperacionSincronizadaDto>([], 0, 2, 20))
        });

        await Servicio(api).Servicio.ObtenerMisOperacionesAsync(resultado, pagina: 2);

        Assert.Equal(url, Assert.Single(api.Pedidos).RutaYConsulta);
    }

    // navegador: lo guardado en el celular; si se pasa uno, es como volver a abrir la app
    
    private static (SincronizacionService Servicio, NavegadorFalso Navegador) Servicio(
        ApiFalsa api, NavegadorFalso? navegador = null)
    {
        navegador ??= new NavegadorFalso();

        var http = new HttpClient(api)
        {
            BaseAddress = new Uri("https://api.prueba/")
        };

        var almacen = new AlmacenLocal(navegador, TimeProvider.System);

        var cola = new ColaOperacionesService(almacen);
        var rutas = new RutasService(http, almacen);
        var confirmadas = new OperacionesConfirmadasService(almacen);

        var sincronizacion = new SincronizacionService(
            http,
            almacen,
            rutas,
            confirmadas,
            cola
        );

        return (sincronizacion, navegador);
    }


    private static OperacionRequest Entrega(int envioId) => new()
    {
        Tipo = "Entrega",
        RutaId = 1,
        EnvioId = envioId,
        FechaHoraDispositivo = DateTime.UtcNow,
        Latitud = -34.9m,
        Longitud = -56.16m
    };

    private static HttpResponseMessage Resultado(string resultado, bool reintentar = false) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SincronizarResponse(
                [new ResultadoOperacionDto(Guid.NewGuid(), resultado, "mensaje", false, reintentar, "EnTransito")]))
        };

    // Simula la API sin red y anota qué le llegó
    private class ApiFalsa(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(string Ruta, string RutaYConsulta, SincronizarRequest? Cuerpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cuerpo = request.Content is null
                ? null
                : await request.Content.ReadFromJsonAsync<SincronizarRequest>(ct);
            Pedidos.Add((request.RequestUri!.AbsolutePath, request.RequestUri.PathAndQuery, cuerpo));

            return responder(request);
        }
    }
}
