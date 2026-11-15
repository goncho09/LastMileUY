using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Deposito;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class RendicionDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static async Task IntentoFallidoAsync(HttpClient repartidor, int rutaId, int envioId)
    {
        var motivos = (await repartidor.GetFromJsonAsync<List<MotivoNoEntregaDto>>("api/sincronizacion/motivos"))!;

        using var respuesta = await repartidor.PostAsJsonAsync("api/sincronizacion/", new SincronizarRequest
        {
            Operaciones =
            [
                new OperacionRequest
                {
                    IdOperacion = Guid.NewGuid(),
                    Tipo = "IntentoFallido",
                    RutaId = rutaId,
                    EnvioId = envioId,
                    FechaHoraDispositivo = DateTime.UtcNow,
                    Latitud = -34.9m,
                    Longitud = -56.16m,
                    MotivoNoEntregaId = motivos[0].Id
                }
            ]
        });

        var resultado = (await respuesta.Content.ReadFromJsonAsync<SincronizarResponse>())!;
        Assert.Equal("Aplicada", Assert.Single(resultado.Resultados).Resultado);
    }

    private static async Task<string> EstadoAsync(HttpClient operador, int envioId) =>
        (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!.Estado;

    private static async Task<Dictionary<int, string>> CodigosAsync(HttpClient despachador, int rutaId) =>
        (await despachador.GetFromJsonAsync<CargaRutaDto>($"api/deposito/cargas/{rutaId}"))!.Bultos
            .ToDictionary(b => b.EnvioId, b => b.Codigo);

    private static async Task<RendicionRutaDto> ConfirmarAsync(HttpClient despachador, int rutaId, params string[] recibidos)
    {
        using var respuesta = await despachador.PostAsJsonAsync($"api/deposito/rendiciones/{rutaId}",
            new ConfirmarRendicionRequest(recibidos, "Rendición de prueba"));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RendicionRutaDto>())!;
    }

    [Fact]
    public async Task El_repartidor_declara_y_el_deposito_confirma_lo_que_vuelve()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var otro = await api.IniciarSesionAsync(Operador2);
        var (rutaId, envios) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 321, 3);
        var codigos = await CodigosAsync(despachador, rutaId);
        var (fallido, sinIntento, perdido) = (envios[0], envios[1], envios[2]);

        using (var antesDeSalir = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/rendicion",
                   new DeclararRendicionRequest([])))
        {
            Assert.Equal(HttpStatusCode.Conflict, antesDeSalir.StatusCode);
        }

        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);
        await IntentoFallidoAsync(repartidor, rutaId, fallido);

        using (var declarada = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/rendicion",
                   new DeclararRendicionRequest([codigos[fallido], codigos[sinIntento].ToLowerInvariant()], "Volví a las 18")))
        {
            Assert.Equal(HttpStatusCode.OK, declarada.StatusCode);
        }

        Assert.Contains((await despachador.GetFromJsonAsync<List<RutaPorRendirDto>>("api/deposito/rendiciones"))!,
            r => r.Id == rutaId && r.EstadoRendicion == EstadosRendicion.Declarada && r.CantidadADevolver == 3);

        var antes = (await despachador.GetFromJsonAsync<RendicionRutaDto>($"api/deposito/rendiciones/{rutaId}"))!;
        Assert.Equal("Volví a las 18", antes.ObservacionesRepartidor);
        Assert.Equal(3, antes.BultosADevolver.Count);
        Assert.False(antes.BultosADevolver.Single(b => b.EnvioId == perdido).Declarado);
        Assert.True(antes.SePuedeConfirmar);

        var rendicion = await ConfirmarAsync(despachador, rutaId, codigos[fallido], codigos[sinIntento], "AJENO-9");

        Assert.Equal(EstadosRendicion.Confirmada, rendicion.EstadoRendicion);
        Assert.Equal("Finalizada", rendicion.EstadoRuta);
        Assert.Equal([codigos[perdido]], rendicion.Faltantes);
        Assert.Equal(["AJENO-9"], rendicion.Sobrantes);
        Assert.Equal("Reprogramado", await EstadoAsync(operador, fallido));
        Assert.Equal("EnDeposito", await EstadoAsync(operador, sinIntento));
        Assert.Equal("EnTransito", await EstadoAsync(operador, perdido));
        Assert.Equal(2, rendicion.Envios.Count);
        Assert.DoesNotContain(await despachador.GetFromJsonAsync<List<RutaPorRendirDto>>("api/deposito/rendiciones") ?? [],
            r => r.Id == rutaId);

        using (var otraVez = await despachador.PostAsJsonAsync($"api/deposito/rendiciones/{rutaId}",
                   new ConfirmarRendicionRequest([])))
        {
            Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        }

        using (var tarde = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/rendicion",
                   new DeclararRendicionRequest([])))
        {
            Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
        }

        using var ajena = await otro.GetAsync($"api/deposito/rendiciones/{rutaId}");
        using var confirmarAjena = await otro.PostAsJsonAsync($"api/deposito/rendiciones/{rutaId}", new ConfirmarRendicionRequest([]));
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, confirmarAjena.StatusCode);
    }

    [Fact]
    public async Task Con_los_intentos_agotados_el_envio_vuelve_al_comercio()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var (rutaId, envios) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 322, 1);
        var envioId = envios[0];
        var codigo = (await CodigosAsync(despachador, rutaId))[envioId];
        var maximo = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!.Regla!.MaximoIntentosEntrega;

        for (var intento = 1; intento <= maximo; intento++)
        {
            if (intento > 1)
            {
                var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
                using var nueva = await despachador.PostAsJsonAsync("api/rutas", new CrearRutaRequest
                {
                    Nombre = $"Segunda salida {intento}",
                    Fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(322 + intento),
                    RepartidorId = opciones.Repartidores[0].Id,
                    VehiculoId = opciones.Vehiculos[0].Id,
                    EnvioIds = [envioId]
                });
                Assert.Equal(HttpStatusCode.Created, nueva.StatusCode);
                rutaId = (await nueva.Content.ReadFromJsonAsync<RutaDetalleDto>())!.Id;
            }

            await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);
            await IntentoFallidoAsync(repartidor, rutaId, envioId);

            var rendicion = await ConfirmarAsync(despachador, rutaId, codigo);
            var esperado = intento < maximo ? "Reprogramado" : "EnDevolucion";

            Assert.Equal(esperado, Assert.Single(rendicion.Envios).EstadoNuevo);
            Assert.Equal(esperado, await EstadoAsync(operador, envioId));
        }
    }
}
