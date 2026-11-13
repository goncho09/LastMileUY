using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Sincronizacion;

public class IndexModel(ISincronizacionApi sincronizacionApi, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Resultado { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public PaginaDto<OperacionSincronizadaDto> Operaciones { get; private set; } = new([], 0, 1, 20);

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Pagina < 1)
        {
            Pagina = 1;
        }

        try
        {
            Operaciones = await sincronizacionApi.ListarAsync(new FiltroOperaciones(Resultado: Resultado, Pagina: Pagina), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener el historial de sincronización");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }
    }

    public Dictionary<string, string?> RutaPagina(int pagina) => new()
    {
        ["resultado"] = Resultado,
        ["pagina"] = pagina.ToString()
    };
}
