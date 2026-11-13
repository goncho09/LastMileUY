using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Sincronizacion;

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
