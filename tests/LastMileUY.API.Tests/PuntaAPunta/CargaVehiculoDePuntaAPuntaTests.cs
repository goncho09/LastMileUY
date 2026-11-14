using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Deposito;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class CargaVehiculoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private const string Administrador = "admin@prueba.test";

    private async Task<CargaRutaDto> VerificarAsync(HttpClient cliente, int rutaId, params string[] bultos)
    {
        using var respuesta = await cliente.PostAsJsonAsync($"api/deposito/cargas/{rutaId}/verificaciones",
            new VerificarCargaRequest(bultos));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CargaRutaDto>())!;
    }

    private async Task CambiarVerificacionObligatoriaAsync(bool activa)
    {
        var administrador = await api.IniciarSesionAsync(Administrador);
        using var cambio = await administrador.PutAsJsonAsync(
            $"api/admin/operadores/{api.IdOperador1}/funcionalidades/{Funcionalidades.VerificacionCarga}",
            new CambiarFuncionalidadRequest { Activa = activa });
        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);
    }

    [Fact]
    public async Task La_verificacion_marca_lo_cargado_y_lo_que_falta()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var (rutaId, envioIds) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 311, 2, bultosPorEnvio: 2);

        var carga = (await despachador.GetFromJsonAsync<CargaRutaDto>($"api/deposito/cargas/{rutaId}"))!;
        Assert.Equal(EstadosCarga.SinVerificar, carga.EstadoCarga);
        Assert.Equal(4, carga.Bultos.Count);
        Assert.Contains((await despachador.GetFromJsonAsync<List<RutaPorCargarDto>>("api/deposito/cargas"))!,
            r => r.Id == rutaId && r.CantidadBultos == 4 && r.CantidadEnvios == 2);

        var codigos = carga.Bultos.Select(b => b.Codigo).ToList();

        var incompleta = await VerificarAsync(despachador, rutaId, codigos[0], codigos[1].ToLowerInvariant(), codigos[2]);
        Assert.Equal(EstadosCarga.Incompleta, incompleta.EstadoCarga);
        Assert.False(incompleta.Bultos.Single(b => b.Codigo == codigos[3]).Cargado);
        Assert.True(incompleta.Bultos.Single(b => b.Codigo == codigos[1]).Cargado);

        var completa = await VerificarAsync(despachador, rutaId, [.. codigos]);
        Assert.Equal(EstadosCarga.Verificada, completa.EstadoCarga);

        Assert.Empty(completa.Sobrantes);
        Assert.Equal(envioIds.Length, completa.Bultos.Select(b => b.EnvioId).Distinct().Count());

        var conSobrante = await VerificarAsync(despachador, rutaId, [.. codigos, "otraruta-1"]);
        Assert.Equal(EstadosCarga.Verificada, conSobrante.EstadoCarga);
        Assert.Equal(["OTRARUTA-1"], conSobrante.Sobrantes);
        Assert.Equal("Deposito", conSobrante.OrigenUltimaVerificacion);
    }

    [Fact]
    public async Task El_repartidor_registra_la_carga_desde_la_app_con_faltantes_y_sobrantes()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var otroRepartidor = await api.IniciarSesionAsync(Repartidor2);
        var (rutaId, _) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 314, 1, bultosPorEnvio: 2);
        var codigos = (await despachador.GetFromJsonAsync<CargaRutaDto>($"api/deposito/cargas/{rutaId}"))!.Bultos
            .Select(b => b.Codigo).ToList();

        using (var antesDeSalir = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/carga", new VerificarCargaRequest(codigos)))
        {
            Assert.Equal(HttpStatusCode.Conflict, antesDeSalir.StatusCode);
        }

        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);

        var escaneadoSinConexion = DateTime.UtcNow.AddMinutes(-20);
        using var respuesta = await repartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/carga",
            new VerificarCargaRequest([codigos[0], "AJENO-1"], null, escaneadoSinConexion));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var carga = (await respuesta.Content.ReadFromJsonAsync<CargaRutaDto>())!;

        Assert.Equal(EstadosCarga.Incompleta, carga.EstadoCarga);
        Assert.Equal("Repartidor", carga.OrigenUltimaVerificacion);
        Assert.Equal(["AJENO-1"], carga.Sobrantes);
        Assert.False(carga.Bultos.Single(b => b.Codigo == codigos[1]).Cargado);
        Assert.Equal(escaneadoSinConexion, carga.UltimaVerificacion!.Value, TimeSpan.FromSeconds(1));

        using var ajena = await otroRepartidor.PostAsJsonAsync($"api/rutas/mias/{rutaId}/carga", new VerificarCargaRequest(codigos));
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
    }

    [Fact]
    public async Task Con_la_verificacion_obligatoria_no_se_despacha_hasta_que_la_carga_esta_completa()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var (rutaId, envioIds) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 312, 2);
        var carga = (await despachador.GetFromJsonAsync<CargaRutaDto>($"api/deposito/cargas/{rutaId}"))!;
        var faltante = carga.Bultos.Single(b => b.EnvioId == envioIds[0]);

        await CambiarVerificacionObligatoriaAsync(true);

        try
        {
            using (var sinVerificar = await despachador.PostAsync($"api/rutas/{rutaId}/despachar", null))
            {
                Assert.Equal(HttpStatusCode.Conflict, sinVerificar.StatusCode);
            }

            await VerificarAsync(despachador, rutaId, carga.Bultos.Where(b => b != faltante).Select(b => b.Codigo).ToArray());

            using (var incompleta = await despachador.PostAsync($"api/rutas/{rutaId}/despachar", null))
            {
                Assert.Equal(HttpStatusCode.Conflict, incompleta.StatusCode);
            }

            using (var quitar = await despachador.PostAsync($"api/deposito/cargas/{rutaId}/envios/{faltante.EnvioId}/quitar", null))
            {
                Assert.Equal(HttpStatusCode.NoContent, quitar.StatusCode);
            }

            var sinElEnvio = (await despachador.GetFromJsonAsync<CargaRutaDto>($"api/deposito/cargas/{rutaId}"))!;
            Assert.Equal(EstadosCarga.Verificada, sinElEnvio.EstadoCarga);
            Assert.DoesNotContain(sinElEnvio.Bultos, b => b.EnvioId == faltante.EnvioId);
            Assert.Equal(1, Assert.Single(sinElEnvio.Bultos).OrdenParada);

            var envioBajado = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{faltante.EnvioId}"))!;
            Assert.Equal("EnDeposito", envioBajado.Estado);

            await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);

            var ruta = (await despachador.GetFromJsonAsync<RutaDetalleDto>($"api/rutas/{rutaId}"))!;
            Assert.Equal("Despachada", ruta.Estado);
            Assert.Equal(1, Assert.Single(ruta.Paradas).Orden);

            using var tarde = await despachador.PostAsJsonAsync($"api/deposito/cargas/{rutaId}/verificaciones",
                new VerificarCargaRequest([]));
            Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
        }
        finally
        {
            await CambiarVerificacionObligatoriaAsync(false);
        }
    }

    [Fact]
    public async Task Sin_la_bandera_se_despacha_como_siempre_y_otro_operador_no_ve_la_carga()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var otro = await api.IniciarSesionAsync(Operador2);
        var (rutaId, _) = await RutasDePuntaAPunta.RutaPlanificadaAsync(api, operador, despachador, 313);

        using var ajena = await otro.GetAsync($"api/deposito/cargas/{rutaId}");
        using var verificarAjena = await otro.PostAsJsonAsync($"api/deposito/cargas/{rutaId}/verificaciones", new VerificarCargaRequest([]));

        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, verificarAjena.StatusCode);
        Assert.DoesNotContain((await otro.GetFromJsonAsync<List<RutaPorCargarDto>>("api/deposito/cargas"))!, r => r.Id == rutaId);

        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);
    }
}
