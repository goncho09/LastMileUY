using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Cache;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Tablero;

// Tablero técnico: aciertos, fallos y porcentaje de aciertos del caché de la API, por caso.
// Se pide a la API cada vez que se abre la página.
public class IndexModel(ICacheApi cacheApi, ILogger<IndexModel> logger) : PageModel
{
    public List<MetricaCacheDto> Metricas { get; private set; } = [];

    public string? Error { get; private set; }

    public string? Mensaje { get; private set; }

    public long TotalAciertos => Metricas.Sum(m => m.Aciertos);

    public long TotalFallos => Metricas.Sum(m => m.Fallos);

    public double PorcentajeTotal =>
        TotalAciertos + TotalFallos == 0 ? 0 : Math.Round(TotalAciertos * 100.0 / (TotalAciertos + TotalFallos), 1);

    public Task OnGetAsync(CancellationToken ct) => CargarAsync(ct);

    // Pone los contadores en cero (por ejemplo, antes de una demostración)
    public async Task<IActionResult> OnPostReiniciarAsync(CancellationToken ct)
    {
        try
        {
            await cacheApi.ReiniciarMetricasAsync(ct);
            Mensaje = "Contadores reiniciados.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudieron reiniciar las métricas del caché");
            Error = "No se pudieron reiniciar los contadores. Probá de nuevo en unos minutos.";
        }

        await CargarAsync(ct);
        return Page();
    }

    private async Task CargarAsync(CancellationToken ct)
    {
        try
        {
            Metricas = await cacheApi.ObtenerMetricasAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "No se pudieron obtener las métricas del caché");
            Error ??= "No se pudieron cargar las métricas del caché. Probá de nuevo en unos minutos.";
        }
    }

    // Ancho de la barra en CSS: con punto decimal aunque la cultura use coma
    public static string Ancho(double porcentaje) =>
        Math.Clamp(porcentaje, 0, 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
