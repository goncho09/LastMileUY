using Microsoft.JSInterop;

namespace LastMileUY.DriverPWA.Tests;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;
using LastMileUY.DriverPWA.Services;

// Cómo la app manda entregas e intentos a la API de sincronización: un solo pedido por operación
// y el mismo IdOperacion en cada reenvío, para que la API no la aplique dos veces.
public class SincronizacionServiceTests
{
    [Fact]
    public async Task Manda_la_operacion_sola_a_la_api_de_sincronizacion()
    {
        var api = new ApiFalsa(_ => Resultado("Aplicada"));
        var servicio = Servicio(api);

        var resultado = await servicio.EnviarAsync(Entrega(envioId: 7));

        Assert.Equal("Aplicada", resultado.Resultado);
        var pedido = Assert.Single(api.Pedidos);
        Assert.Equal("/api/sincronizacion", pedido.Ruta);
        var operacion = Assert.Single(pedido.Cuerpo!.Operaciones);
        Assert.Equal(7, operacion.EnvioId);
        Assert.NotEqual(Guid.Empty, operacion.IdOperacion);
    }

    [Fact]
    public async Task Si_no_llega_la_respuesta_el_reenvio_lleva_el_mismo_id()
    {
        var primera = true;
        var api = new ApiFalsa(_ =>
        {
            if (!primera)
                return Resultado("Aplicada");

            primera = false;
            throw new HttpRequestException("Sin conexión");
        });
        var servicio = Servicio(api);

        await Assert.ThrowsAsync<HttpRequestException>(() => servicio.EnviarAsync(Entrega(envioId: 7)));
        await servicio.EnviarAsync(Entrega(envioId: 7));

        Assert.Equal(2, api.Pedidos.Count);
        Assert.Equal(api.Pedidos[0].Cuerpo!.Operaciones[0].IdOperacion, api.Pedidos[1].Cuerpo!.Operaciones[0].IdOperacion);
    }

    [Fact]
    public async Task Si_la_api_pide_reintentar_el_reenvio_lleva_el_mismo_id()
    {
        var respuestas = new Queue<HttpResponseMessage>([Resultado("Error", reintentar: true), Resultado("Aplicada")]);
        var api = new ApiFalsa(_ => respuestas.Dequeue());
        var servicio = Servicio(api);

        await servicio.EnviarAsync(Entrega(envioId: 7));
        await servicio.EnviarAsync(Entrega(envioId: 7));

        Assert.Equal(api.Pedidos[0].Cuerpo!.Operaciones[0].IdOperacion, api.Pedidos[1].Cuerpo!.Operaciones[0].IdOperacion);
    }

    [Theory]
    [InlineData("Aplicada")]
    [InlineData("Rechazada")]
    [InlineData("Conflicto")]
    public async Task Con_un_resultado_final_lo_proximo_es_una_operacion_nueva(string resultado)
    {
        // Rechazada: el repartidor corrige (por ejemplo, agrega la firma) y la manda de nuevo.
        // Con el mismo id la API devolvería el rechazo anterior sin mirarla.
        var api = new ApiFalsa(_ => Resultado(resultado));
        var servicio = Servicio(api);

        await servicio.EnviarAsync(Entrega(envioId: 7));
        await servicio.EnviarAsync(Entrega(envioId: 7));

        Assert.NotEqual(api.Pedidos[0].Cuerpo!.Operaciones[0].IdOperacion, api.Pedidos[1].Cuerpo!.Operaciones[0].IdOperacion);
    }

    [Fact]
    public async Task Cada_envio_tiene_su_propia_operacion()
    {
        var api = new ApiFalsa(_ => throw new HttpRequestException("Sin conexión"));
        var servicio = Servicio(api);

        await Assert.ThrowsAsync<HttpRequestException>(() => servicio.EnviarAsync(Entrega(envioId: 7)));
        await Assert.ThrowsAsync<HttpRequestException>(() => servicio.EnviarAsync(Entrega(envioId: 8)));

        Assert.NotEqual(api.Pedidos[0].Cuerpo!.Operaciones[0].IdOperacion, api.Pedidos[1].Cuerpo!.Operaciones[0].IdOperacion);
    }

    [Fact]
    public async Task Con_la_sesion_vencida_avisa_para_volver_a_iniciar()
    {
        var servicio = Servicio(new ApiFalsa(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.EnviarAsync(Entrega(envioId: 7)));
    }

    [Fact]
    public async Task Los_motivos_salen_de_la_api_de_sincronizacion()
    {
        var api = new ApiFalsa(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new { id = 1, nombre = "Ausente", descripcion = (string?)null } })
        });

        var motivos = await Servicio(api).ObtenerMotivosAsync();

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

        await Servicio(api).ObtenerMisOperacionesAsync(resultado, pagina: 2);

        Assert.Equal(url, Assert.Single(api.Pedidos).RutaYConsulta);
    }

    
    private static SincronizacionService Servicio(ApiFalsa api)
    {
        var http = new HttpClient(api)
        {
            BaseAddress = new Uri("https://api.prueba/")
        };

        var almacen = new AlmacenLocal(
            new NavegadorFalso(),
            TimeProvider.System
        );

        var cola = new ColaOperacionesService(almacen);

        return new SincronizacionService(http, almacen, cola);
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

    
    private class NavegadorFalso : IJSRuntime
    {
        private readonly Dictionary<string, string> datos = [];

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args)
        {
            return InvokeAsync<TValue>(
                identifier,
                CancellationToken.None,
                args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            object? resultado = null;

            switch (identifier)
            {
                case "lastMileAlmacen.guardar":
                    datos[(string)args![0]!] = (string)args[1]!;
                    break;

                case "lastMileAlmacen.leer":
                    resultado = datos.GetValueOrDefault(
                        (string)args![0]!);
                    break;

                case "lastMileAlmacen.borrar":
                    datos.Remove((string)args![0]!);
                    break;

                case "lastMileAlmacen.borrarTodo":
                    datos.Clear();
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Función inesperada: {identifier}");
            }

            return ValueTask.FromResult((TValue)resultado!);
        }
    }

    
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
