using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Reportes;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ReportesDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private static string Periodo()
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HoraUruguay));
        return $"desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}";
    }

    private static async Task SincronizarAsync(HttpClient repartidor, OperacionRequest operacion)
    {
        using var respuesta = await repartidor.PostAsJsonAsync("api/sincronizacion/", new SincronizarRequest { Operaciones = [operacion] });
        Assert.Equal("Aplicada", Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizarResponse>())!.Resultados).Resultado);
    }

    [Fact]
    public async Task El_reporte_cuenta_entregas_fallidas_y_liquida_por_comercio()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);

        var antes = (await despachador.GetFromJsonAsync<ReporteGestionDto>($"api/reportes?{Periodo()}"))!;

        var (rutaId, envios) = await RutasDePuntaAPunta.RutaConEnviosAsync(api, operador, despachador, 351, 2);
        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);
        var motivo = (await repartidor.GetFromJsonAsync<List<MotivoNoEntregaDto>>("api/sincronizacion/motivos"))![0];

        await SincronizarAsync(repartidor, new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(), Tipo = "Entrega", RutaId = rutaId, EnvioId = envios[0],
            FechaHoraDispositivo = DateTime.UtcNow, Latitud = -34.9m, Longitud = -56.16m,
            NombreReceptor = "Ana Silva", DocumentoReceptor = "1.234.567-8", FirmaBase64 = Png, FotoBase64 = Png
        });

        await SincronizarAsync(repartidor, new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(), Tipo = "IntentoFallido", RutaId = rutaId, EnvioId = envios[1],
            FechaHoraDispositivo = DateTime.UtcNow, Latitud = -34.9m, Longitud = -56.16m, MotivoNoEntregaId = motivo.Id
        });

        var despues = (await despachador.GetFromJsonAsync<ReporteGestionDto>($"api/reportes?{Periodo()}"))!;

        Assert.Equal(antes.TotalEnvios + 2, despues.TotalEnvios);
        Assert.Equal(Entregados(antes) + 1, Entregados(despues));
        Assert.Equal(Motivo(antes, motivo.Nombre) + 1, Motivo(despues, motivo.Nombre));
        Assert.Equal(Intentos(antes) + 2, Intentos(despues));
        Assert.Equal(despues.TotalEnvios, despues.Volumen.Sum(v => v.Envios));
        Assert.Equal(despues.TotalEnvios, despues.Liquidacion.Sum(l => l.Envios));

        var cuenta = (await comercio.GetFromJsonAsync<ReporteGestionDto>($"api/reportes/mi-cuenta?{Periodo()}"))!;
        Assert.Single(cuenta.Liquidacion);
        Assert.Empty(cuenta.PorRepartidor);

        using var csv = await despachador.GetAsync($"api/reportes/liquidacion.csv?{Periodo()}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("comercio;envios", (await csv.Content.ReadAsStringAsync()).TrimStart('﻿'));

        using var alReves = await despachador.GetAsync("api/reportes?desde=2026-11-10&hasta=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, alReves.StatusCode);

        using var comercioAlTablero = await comercio.GetAsync($"api/reportes?{Periodo()}");
        Assert.Equal(HttpStatusCode.Forbidden, comercioAlTablero.StatusCode);
    }

    private static int Entregados(ReporteGestionDto r) => r.PorComercio.Sum(c => c.Entregados);

    private static int Intentos(ReporteGestionDto r) => r.PorRepartidor.Sum(c => c.Intentos);

    private static int Motivo(ReporteGestionDto r, string nombre) => r.Motivos.FirstOrDefault(m => m.Motivo == nombre)?.Cantidad ?? 0;
}
