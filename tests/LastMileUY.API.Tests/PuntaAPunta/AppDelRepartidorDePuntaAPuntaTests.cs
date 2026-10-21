namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ApiDePuntaAPunta;

// Lo que usa la app del repartidor, por HTTP y con los usuarios reales: la hoja de ruta (con la
// evidencia que pide cada parada), su caché y su invalidación, y qué rutas puede abrir.
// Las pruebas corren sin Redis: la API usa el caché en memoria, con la misma interfaz.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class AppDelRepartidorDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task La_hoja_cacheada_se_actualiza_apenas_el_repartidor_registra_una_entrega()
    {
        var (operador, despachador, repartidor) = await SesionesAsync();
        // Dos envíos: con uno solo, al entregarlo la ruta se finaliza y ya no se descarga
        var (rutaId, envioIds) = await RutasDePuntaAPunta.RutaConEnviosAsync(
            api, operador, despachador, diasAdelante: 3, cantidadEnvios: 2);
        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);

        // Primera descarga: queda en el caché (dura un minuto)
        var antes = (await repartidor.GetFromJsonAsync<RutaDetalleDto>($"api/rutas/{rutaId}/hoja"))!;
        Assert.Equal("Despachada", antes.Estado);
        Assert.All(antes.Paradas, p => Assert.Equal("EnTransito", p.EstadoEnvio));

        var resultado = await SincronizacionDePuntaAPuntaTests.SincronizarAsync(repartidor, Entrega(rutaId, envioIds[0]));
        Assert.Equal("Aplicada", resultado.Resultado);

        // Sin la invalidación seguiría saliendo la hoja vieja del caché hasta que venza
        var despues = (await repartidor.GetFromJsonAsync<RutaDetalleDto>($"api/rutas/{rutaId}/hoja"))!;
        Assert.Equal("EnCurso", despues.Estado);
        Assert.Equal("Entregado", despues.Paradas.Single(p => p.EnvioId == envioIds[0]).EstadoEnvio);
        Assert.Equal("EnTransito", despues.Paradas.Single(p => p.EnvioId == envioIds[1]).EstadoEnvio);
    }

    [Fact]
    public async Task La_hoja_cacheada_no_le_llega_a_otro_repartidor()
    {
        var (operador, despachador, repartidor) = await SesionesAsync();
        var otro = await api.IniciarSesionAsync(Repartidor2);
        var (rutaId, _) = await RutasDePuntaAPunta.RutaDespachadaAsync(api, operador, despachador, diasAdelante: 4);

        using (var propia = await repartidor.GetAsync($"api/rutas/{rutaId}/hoja"))
        {
            Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
        }

        // Ya está en el caché, pero los controles se hacen antes contra la base
        using var ajena = await otro.GetAsync($"api/rutas/{rutaId}/hoja");
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
    }

    [Fact]
    public async Task Cada_parada_dice_que_evidencia_pide_la_regla_del_operador()
    {
        var (operador, despachador, repartidor) = await SesionesAsync();
        var (rutaId, _) = await RutasDePuntaAPunta.RutaDespachadaAsync(api, operador, despachador, diasAdelante: 5);

        var hoja = (await repartidor.GetFromJsonAsync<RutaDetalleDto>($"api/rutas/{rutaId}/hoja"))!;
        var parada = Assert.Single(hoja.Paradas);

        // La misma regla que aplica la API al sincronizar (la del operador 1 pide firma)
        var reglas = (await operador.GetFromJsonAsync<OperadorActualDto>("api/operador"))!.Reglas;
        var regla = Assert.Single(reglas, r => r.Modalidad == parada.Modalidad);

        Assert.True(parada.Evidencia.Firma);
        Assert.Equal(
            new EvidenciaRequeridaDto(regla.RequiereFirma, regla.RequiereFotografia, regla.RequiereDocumentoReceptor),
            parada.Evidencia);

        // Lo que pide la hoja es lo que controla la sincronización: sin firma, se rechaza
        var sinFirma = Entrega(rutaId, parada.EnvioId);
        sinFirma.FirmaBase64 = null;
        var rechazada = await SincronizacionDePuntaAPuntaTests.SincronizarAsync(repartidor, sinFirma);
        Assert.Equal("Rechazada", rechazada.Resultado);
        Assert.Contains("firma", rechazada.Mensaje);
    }

    [Fact]
    public async Task El_repartidor_abre_desde_la_app_solo_sus_rutas_ya_despachadas()
    {
        var (operador, despachador, repartidor) = await SesionesAsync();
        var otro = await api.IniciarSesionAsync(Repartidor2);
        var (rutaId, envioId) = await RutasDePuntaAPunta.RutaPlanificadaAsync(api, operador, despachador, diasAdelante: 6);

        // Planificada: el despachador todavía la está armando
        using (var planificada = await repartidor.GetAsync($"api/rutas/mias/{rutaId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, planificada.StatusCode);
        }

        using (var despacho = await despachador.PostAsync($"api/rutas/{rutaId}/despachar", null))
        {
            Assert.Equal(HttpStatusCode.OK, despacho.StatusCode);
        }

        using (var despachada = await repartidor.GetAsync($"api/rutas/mias/{rutaId}"))
        {
            Assert.Equal(HttpStatusCode.OK, despachada.StatusCode);
        }

        using (var ajena = await otro.GetAsync($"api/rutas/mias/{rutaId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        }

        // Terminada la última parada, la sigue viendo (la app muestra "Ruta finalizada")
        await SincronizacionDePuntaAPuntaTests.SincronizarAsync(repartidor, Entrega(rutaId, envioId));

        var finalizada = (await repartidor.GetFromJsonAsync<RutaDetalleDto>($"api/rutas/mias/{rutaId}"))!;
        Assert.Equal("Finalizada", finalizada.Estado);
    }

    [Fact]
    public async Task La_configuracion_del_operador_cacheada_se_actualiza_al_cambiar_y_no_se_mezcla_entre_operadores()
    {
        var operador1 = await api.IniciarSesionAsync(Operador1);
        var operador2 = await api.IniciarSesionAsync(Operador2);

        var original = (await operador1.GetFromJsonAsync<OperadorActualDto>("api/operador"))!;
        var delOtro = (await operador2.GetFromJsonAsync<OperadorActualDto>("api/operador"))!;

        // Cada operador tiene su clave en el caché
        Assert.Equal(api.IdOperador1, original.Id);
        Assert.Equal(api.IdOperador2, delOtro.Id);

        // Un cambio guardado con EF (como lo haría la pantalla de configuración) invalida el caché
        await CambiarColorAsync(api.IdOperador1, "#123456");

        try
        {
            var cambiado = (await operador1.GetFromJsonAsync<OperadorActualDto>("api/operador"))!;
            Assert.Equal("#123456", cambiado.ColorPrimario);
        }
        finally
        {
            await CambiarColorAsync(api.IdOperador1, original.ColorPrimario);
        }
    }

    private async Task CambiarColorAsync(int operadorId, string? color)
    {
        using var alcance = api.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var operador = await db.Set<Operador>().SingleAsync(o => o.Id == operadorId);
        operador.ColorPrimario = color;
        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Operador, HttpClient Despachador, HttpClient Repartidor)> SesionesAsync() =>
        (await api.IniciarSesionAsync(Operador1),
            await api.IniciarSesionAsync(Despachador1),
            await api.IniciarSesionAsync(Repartidor1));

    // Una entrega con firma (la regla del operador 1 la pide)
    private static OperacionRequest Entrega(int rutaId, int envioId) => new()
    {
        IdOperacion = Guid.NewGuid(),
        Tipo = "Entrega",
        RutaId = rutaId,
        EnvioId = envioId,
        FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-5),
        Latitud = -34.9m,
        Longitud = -56.16m,
        NombreReceptor = "Ana Silva",
        FirmaBase64 = Png
    };
}
