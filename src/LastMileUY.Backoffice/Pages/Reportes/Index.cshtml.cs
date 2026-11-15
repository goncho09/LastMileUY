using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Reportes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Reportes;

public class IndexModel(IReportesApi api, ILogger<IndexModel> logger) : PageModel
{
    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    public ReporteGestionDto? Reporte { get; private set; }

    public string? Error { get; private set; }

    public int MaximoVolumen => Math.Max(1, Reporte?.Volumen.Select(v => v.Envios).DefaultIfEmpty(0).Max() ?? 1);

    private void CompletarPeriodo()
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HoraUruguay));
        Hasta ??= hoy;
        Desde ??= Hasta.Value.AddDays(-29);
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        CompletarPeriodo();

        try
        {
            Reporte = await api.ObtenerAsync(Desde!.Value, Hasta!.Value, ct);
        }
        catch (OperacionRechazadaException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "No se pudo cargar el reporte de gestión");
            Error = "No se pudo cargar el reporte. Probá de nuevo en unos minutos.";
        }
    }

    public async Task<IActionResult> OnGetCsvAsync(CancellationToken ct)
    {
        CompletarPeriodo();

        try
        {
            var csv = await api.LiquidacionCsvAsync(Desde!.Value, Hasta!.Value, ct);
            return File(csv, "text/csv; charset=utf-8", $"liquidacion-{Desde:yyyyMMdd}-{Hasta:yyyyMMdd}.csv");
        }
        catch (OperacionRechazadaException ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    public static string Porcentaje(decimal valor) => $"{valor:0.#} %";
}
