using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Reportes;

namespace LastMileUY.Backoffice.Servicios;

public interface IReportesApi
{
    Task<ReporteGestionDto> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);

    Task<byte[]> LiquidacionCsvAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}

public sealed class ReportesApiHttp(HttpClient http) : IReportesApi
{
    public async Task<ReporteGestionDto> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"api/reportes?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}", ct);

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            throw new OperacionRechazadaException(await RespuestasApi.LeerMensajeAsync(respuesta, "El período no es válido.", ct));

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<ReporteGestionDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el reporte.");
    }

    public async Task<byte[]> LiquidacionCsvAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"api/reportes/liquidacion.csv?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}", ct);

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            throw new OperacionRechazadaException(await RespuestasApi.LeerMensajeAsync(respuesta, "El período no es válido.", ct));

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsByteArrayAsync(ct);
    }
}

public sealed class ReportesApiDePrueba : IReportesApi
{
    public Task<ReporteGestionDto> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        if (hasta < desde)
            throw new OperacionRechazadaException("La fecha final no puede ser anterior a la inicial.");

        var dias = Enumerable.Range(0, hasta.DayNumber - desde.DayNumber + 1)
            .Select(i => new VolumenDiaDto(desde.AddDays(i), 20 + i * 7 % 13, 15 + i * 5 % 11))
            .ToList();

        return Task.FromResult(new ReporteGestionDto(
            desde, hasta, dias.Sum(d => d.Envios), dias,
            [new("Centro", 60, 52, 47, 5), new("Pocitos", 41, 33, 30, 6), new("Sin zona", 4, 2, 2, 1)],
            [new("Tienda Sur", 70, 61, 55, 6), new("Libros del Este", 35, 26, 24, 6)],
            [new("Carlos Méndez", 48, 41), new("Lucía Fernández", 39, 30)],
            [new("Destinatario ausente", 9), new("Dirección incorrecta", 4), new("Rechazado por el destinatario", 2)],
            [new(1, "Libros del Este", 35, 26, 4, 1, 8420.50m), new(2, "Tienda Sur", 70, 61, 3, 0, 15980m)]));
    }

    public Task<byte[]> LiquidacionCsvAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        Task.FromResult("comercio;envios;entregados;devueltos;sin_tarifa;importe\nLibros del Este;35;26;4;1;8420.50\nTienda Sur;70;61;3;0;15980.00\n"u8.ToArray());
}
