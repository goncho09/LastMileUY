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
