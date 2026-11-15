using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Seguimiento;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ReprogramacionPublicaDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private static DateOnly Hoy() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HoraUruguay));

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

        Assert.Equal("Aplicada", Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizarResponse>())!.Resultados).Resultado);
    }

    private static async Task<HttpResponseMessage> CrearRutaAsync(HttpClient despachador, int envioId, DateOnly fecha)
    {
        var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;

        return await despachador.PostAsJsonAsync("api/rutas", new CrearRutaRequest
        {
            Nombre = $"Reprogramada {fecha:dd/MM}",
            Fecha = fecha,
            RepartidorId = opciones.Repartidores[^1].Id,
            VehiculoId = opciones.Vehiculos[^1].Id,
            EnvioIds = [envioId]
        });
    }

    [Fact]
    public async Task El_destinatario_reprograma_un_envio_no_entregado_segun_las_reglas_del_operador()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var publico = api.ClienteSinSesion();
        var (rutaId, envios) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 331, 1);
        var envioId = envios[0];
        var codigo = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!.CodigoSeguimiento;

        var asignado = (await publico.GetFromJsonAsync<SeguimientoPublicoDto>($"api/seguimiento/{codigo}"))!;
        Assert.False(asignado.Reprogramacion!.Permitida);
        Assert.Contains("en camino", asignado.Reprogramacion.Motivo);

        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);
        await IntentoFallidoAsync(repartidor, rutaId, envioId);

        var fallido = (await publico.GetFromJsonAsync<SeguimientoPublicoDto>($"api/seguimiento/{codigo}"))!;
        Assert.Equal("NoEntregado", fallido.Estado);
        Assert.True(fallido.Reprogramacion!.Permitida);
        Assert.True(fallido.Reprogramacion.FechaMinima > Hoy());

        using (var muyLejos = await publico.PostAsJsonAsync($"api/seguimiento/{codigo}/reprogramacion",
                   new SolicitarReprogramacionRequest(Hoy().AddDays(60))))
        {
            Assert.Equal(HttpStatusCode.Conflict, muyLejos.StatusCode);
        }

        using (var franjaInventada = await publico.PostAsJsonAsync($"api/seguimiento/{codigo}/reprogramacion",
                   new SolicitarReprogramacionRequest(Hoy().AddDays(9), "De madrugada")))
        {
            Assert.Equal(HttpStatusCode.Conflict, franjaInventada.StatusCode);
        }

        var pedida = Hoy().AddDays(9);

        using (var pedido = await publico.PostAsJsonAsync($"api/seguimiento/{codigo}/reprogramacion",
                   new SolicitarReprogramacionRequest(pedida, null, "Estoy de tarde")))
        {
            Assert.Equal(HttpStatusCode.NoContent, pedido.StatusCode);
        }

        var reprogramado = (await publico.GetFromJsonAsync<SeguimientoPublicoDto>($"api/seguimiento/{codigo}"))!;
        Assert.Equal("Reprogramado", reprogramado.Estado);
        Assert.Equal(pedida, reprogramado.Reprogramacion!.FechaSolicitada);
        Assert.False(reprogramado.Reprogramacion.Permitida);

        var historial = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!;
        Assert.Equal("Reprogramado", historial.Estado);

        using (var otraVez = await publico.PostAsJsonAsync($"api/seguimiento/{codigo}/reprogramacion",
                   new SolicitarReprogramacionRequest(pedida.AddDays(1))))
        {
            Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        }

        using (var antes = await CrearRutaAsync(despachador, envioId, pedida.AddDays(-1)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, antes.StatusCode);
            Assert.Contains("pidió la entrega", await antes.Content.ReadAsStringAsync());
        }

        using (var elDia = await CrearRutaAsync(despachador, envioId, pedida))
        {
            Assert.Equal(HttpStatusCode.Created, elDia.StatusCode);
        }
    }

    [Fact]
    public async Task Un_codigo_que_no_existe_da_404()
    {
        using var respuesta = await api.ClienteSinSesion().PostAsJsonAsync("api/seguimiento/NOEXISTE1234/reprogramacion",
            new SolicitarReprogramacionRequest(Hoy().AddDays(3)));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
