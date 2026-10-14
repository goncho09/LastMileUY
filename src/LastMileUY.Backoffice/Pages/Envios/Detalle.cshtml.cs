using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Envios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Envios;

// Detalle de un envío: estado, destinatario, dirección, bultos e historial
public class DetalleModel(IEnviosApi enviosApi, ILogger<DetalleModel> logger) : PageModel
{
    public EnvioDetalleDto? Envio { get; private set; }

    public string? Error { get; private set; }

    // Resultado del último cambio de estado (sobrevive a la redirección)
    [TempData]
    public string? Aviso { get; set; }

    [TempData]
    public string? ErrorCambio { get; set; }

    // Cambio de estado: después de guardar se redirige al detalle (así recargar la página no lo repite)
    public async Task<IActionResult> OnPostCambiarEstadoAsync(
        int id, string? nuevoEstado, string? comentario, CancellationToken ct)
    {
        // El formulario no se muestra sin permiso, pero el POST se puede armar a mano
        if (!Permisos.PuedeCambiarEstado(User))
        {
            ErrorCambio = "Tu perfil no puede cambiar el estado de los envíos.";
            return RedirectToPage(new { id });
        }

        if (string.IsNullOrWhiteSpace(nuevoEstado))
        {
            ErrorCambio = "Elegí el estado nuevo.";
            return RedirectToPage(new { id });
        }

        try
        {
            var envio = await enviosApi.CambiarEstadoAsync(id, nuevoEstado, comentario, ct);

            if (envio is null)
            {
                return RedirectToPage(new { id });
            }

            Aviso = $"El envío pasó a: {EstadosEnvio.Texto(envio.Estado)}.";
        }
        catch (OperacionRechazadaException ex)
        {
            ErrorCambio = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo cambiar el estado del envío {EnvioId}", id);
            ErrorCambio = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        return RedirectToPage(new { id });
    }

    public async Task OnGetAsync(int id, CancellationToken ct)
    {
        try
        {
            Envio = await enviosApi.ObtenerAsync(id, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener el envío {EnvioId}", id);
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
            return;
        }

        // Si no existe se responde 404, pero con un mensaje en vez de una página en blanco
        if (Envio is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
