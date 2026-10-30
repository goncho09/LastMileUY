using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.MensajesFallidos;

[Authorize(Roles = "Administrador")]
public class DetalleModel(
    IMensajesFallidosApi mensajesApi,
    ILogger<DetalleModel> logger) : PageModel
{
    public MensajeFallidoDetalleResponse? Mensaje { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return NotFound();
        }

        try
        {
            Mensaje = await mensajesApi.ObtenerDetalleAsync(id, ct);

            if (Mensaje is null)
            {
                return NotFound();
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException
                or TaskCanceledException
                or InvalidOperationException)
        {
            logger.LogError(
                ex,
                "No se pudo consultar el mensaje fallido {MensajeId}",
                id);

            Error = "No se pudo obtener el detalle del mensaje fallido.";
        }

        return Page();
    }
}