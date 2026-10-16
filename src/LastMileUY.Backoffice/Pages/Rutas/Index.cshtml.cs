using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Rutas;

// Listado de hojas de ruta del operador, con filtro por fecha y estado
public class IndexModel(IRutasApi rutasApi, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? Fecha { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public PaginaDto<RutaResumenDto> Resultado { get; private set; } = new([], 0, 1, 20);

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Pagina < 1)
        {
            Pagina = 1;
        }

        try
        {
            Resultado = await rutasApi.ListarAsync(new FiltroRutas(Fecha, Estado, Pagina), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener el listado de hojas de ruta");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }
    }

    // Parámetros de la URL para otra página, conservando los filtros
    public Dictionary<string, string?> RutaPagina(int pagina) => new()
    {
        ["fecha"] = Fecha?.ToString("yyyy-MM-dd"),
        ["estado"] = Estado,
        ["pagina"] = pagina.ToString()
    };
}
