using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Flota;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class FlotaDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    [Fact]
    public async Task El_repartidor_manda_su_posicion_y_el_tablero_muestra_la_ultima()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var otroRepartidor = await api.IniciarSesionAsync(Repartidor2);
        var otroOperador = await api.IniciarSesionAsync(Operador2);
        var (rutaId, _) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 341, 2);

        var antes = new RegistrarPosicionesRequest([new PosicionRequest(-34.9m, -56.16m, DateTime.UtcNow)]);

        using (var planificada = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/posiciones", antes))
        {
            Assert.Equal(HttpStatusCode.Conflict, planificada.StatusCode);
        }

        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);

        var hace = DateTime.UtcNow.AddMinutes(-10);
        var lote = new RegistrarPosicionesRequest(
        [
            new PosicionRequest(-34.90m, -56.16m, hace),
            new PosicionRequest(-34.91m, -56.17m, hace.AddMinutes(5)),
            new PosicionRequest(-34.91m, -56.17m, hace.AddMinutes(5)),
            new PosicionRequest(95m, -56.17m, hace.AddMinutes(6)),
            new PosicionRequest(-34.92m, -56.18m, DateTime.UtcNow.AddHours(3))
        ]);

        using (var respuesta = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/posiciones", lote))
        {
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var resultado = (await respuesta.Content.ReadFromJsonAsync<RegistrarPosicionesResponse>())!;
            Assert.Equal(2, resultado.Guardadas);
            Assert.Equal(3, resultado.Descartadas);
        }

        using (var reenvio = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/posiciones", lote))
        {
            Assert.Equal(0, (await reenvio.Content.ReadFromJsonAsync<RegistrarPosicionesResponse>())!.Guardadas);
        }

        var flota = (await despachador.GetFromJsonAsync<FlotaDto>("api/flota"))!;
        var ruta = Assert.Single(flota.Rutas, r => r.RutaId == rutaId);
        Assert.Equal(-34.91m, ruta.Latitud);
        Assert.Equal(-56.17m, ruta.Longitud);
        Assert.Equal(2, ruta.Paradas);
        Assert.Equal(2, ruta.Pendientes);

        using var ajena = await otroRepartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/posiciones", lote);
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.DoesNotContain((await otroOperador.GetFromJsonAsync<FlotaDto>("api/flota"))!.Rutas, r => r.RutaId == rutaId);

        using var sinPermiso = await repartidor.GetAsync("api/flota");
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
    }
}
