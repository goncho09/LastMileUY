using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Envios;

// Listado de envíos del operador, con filtro por estado, búsqueda y paginación
public class IndexModel(IEnviosApi enviosApi, ILogger<IndexModel> logger) : PageModel
{
    // Los filtros vienen en la URL (?estado=...&buscar=...&pagina=...)
    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Buscar { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public PaginaDto<EnvioResumenDto> Resultado { get; private set; } = new([], 0, 1, 20);

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Pagina < 1)
        {
            Pagina = 1;
        }

        try
        {
            Resultado = await enviosApi.ListarAsync(new FiltroEnvios(Estado, Buscar, Pagina), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener el listado de envíos");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }
    }

    // Arma los parámetros de la URL para otra página, conservando los filtros
    public Dictionary<string, string?> RutaPagina(int pagina) => new()
    {
        ["estado"] = Estado,
        ["buscar"] = Buscar,
        ["pagina"] = pagina.ToString()
    };
}
