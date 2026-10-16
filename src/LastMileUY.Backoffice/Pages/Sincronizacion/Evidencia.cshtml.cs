using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Sincronizacion;

// La firma o la foto de una entrega. El navegador no puede pedírsela a la API directamente
// (la sesión con la API la tiene el Backoffice), así que el Backoffice la busca y la devuelve.
public class EvidenciaModel(ISincronizacionApi sincronizacionApi) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int intentoId, string tipo, CancellationToken ct)
    {
        if (tipo is not ("firma" or "foto"))
        {
            return NotFound();
        }

        var evidencia = await sincronizacionApi.ObtenerEvidenciaAsync(intentoId, tipo, ct);

        return evidencia is null
            ? NotFound()
            : File(evidencia.Contenido, evidencia.TipoContenido);
    }
}
