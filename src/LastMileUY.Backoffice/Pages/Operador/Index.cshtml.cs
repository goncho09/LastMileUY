using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Operadores;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Operador;

// Identidad y configuración del operador logueado: se pide a la API en cada visita,
// así muestra los datos al día (el color del encabezado se toma al iniciar sesión).
public class IndexModel(IOperadorApi operadorApi, ILogger<IndexModel> logger) : PageModel
{
    public OperadorActualDto? Operador { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Operador = await operadorApi.ObtenerActualAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "No se pudieron obtener los datos del operador");
            Error = "No se pudieron cargar los datos del operador. Probá de nuevo en unos minutos.";
        }
    }
}
