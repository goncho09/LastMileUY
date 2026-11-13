using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public abstract class PaginaConfiguracion(ILogger logger) : PageModel
{
    public string? Error { get; protected set; }

    protected abstract Task CargarAsync(CancellationToken ct);

    public virtual async Task OnGetAsync(CancellationToken ct) => await CargarConAvisoAsync(ct);

    protected async Task CargarConAvisoAsync(CancellationToken ct)
    {
        try
        {
            await CargarAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo cargar {Pagina}", GetType().Name);
            Error ??= "No se pudieron cargar los datos. Probá de nuevo en unos minutos.";
        }
    }

    protected async Task<IActionResult> EjecutarAsync(Func<Task> accion, string aviso, object? rutaAlTerminar, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await accion();
                TempData["Aviso"] = aviso;
                return RedirectToPage(rutaAlTerminar);
            }
            catch (OperacionRechazadaException ex)
            {
                Error = ex.Message;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogError(ex, "No se pudo guardar en {Pagina}", GetType().Name);
                Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
            }
        }

        await CargarConAvisoAsync(ct);
        return Page();
    }
}
