using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Envios;

public class IndexModel(IEnviosApi enviosApi, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Buscar { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Canal { get; set; }

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
            Resultado = await enviosApi.ListarAsync(new FiltroEnvios(Estado, Buscar, Pagina, Canal: Canal), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener el listado de envíos");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }
    }

    public Dictionary<string, string?> RutaPagina(int pagina) => new()
    {
        ["estado"] = Estado,
        ["buscar"] = Buscar,
        ["canal"] = Canal,
        ["pagina"] = pagina.ToString()
    };
}
