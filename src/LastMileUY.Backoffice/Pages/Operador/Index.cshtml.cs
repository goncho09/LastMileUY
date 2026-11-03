using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Operadores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Operador;

// Identidad y configuración del operador logueado: se pide a la API en cada visita,
// así muestra los datos al día (el color del encabezado se toma al iniciar sesión).
// También las reglas de entrega parcial: el historial de versiones y el formulario para publicar una nueva.
public class IndexModel(IOperadorApi operadorApi, ILogger<IndexModel> logger) : PageModel
{
    public OperadorActualDto? Operador { get; private set; }

    // La más nueva primero: la primera es la vigente
    public IReadOnlyList<ReglaEntregaParcialDto> ReglasEntregaParcial { get; private set; } = [];

    public string? Error { get; private set; }

    public string? ErrorReglasEntregaParcial { get; private set; }

    // Resultado de la última publicación (sobrevive a la redirección)
    [TempData]
    public string? Aviso { get; set; }

    [TempData]
    public string? ErrorPublicacion { get; set; }

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
            return;
        }

        try
        {
            ReglasEntregaParcial = await operadorApi.ListarReglasEntregaParcialAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudieron obtener las reglas de entrega parcial");
            ErrorReglasEntregaParcial = "No se pudieron cargar las reglas de entrega parcial.";
        }
    }

    // Publicar una versión nueva: después se redirige a la página (así recargar no la publica otra vez)
    public async Task<IActionResult> OnPostPublicarReglaEntregaParcialAsync(
        bool permiteEntregaParcial, string? destinoBultosPendientes, string? motivo, CancellationToken ct)
    {
        try
        {
            var regla = await operadorApi.PublicarReglaEntregaParcialAsync(new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = permiteEntregaParcial,
                DestinoBultosPendientes = destinoBultosPendientes ?? string.Empty,
                Motivo = motivo ?? string.Empty
            }, ct);

            Aviso = $"Se publicó la versión {regla.Version}. Las hojas de ruta que se despachen desde ahora la usan; "
                + "las que ya salieron siguen con la suya.";
        }
        catch (OperacionRechazadaException ex)
        {
            ErrorPublicacion = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo publicar la regla de entrega parcial");
            ErrorPublicacion = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        // De vuelta en la sección, donde está el aviso
        return RedirectToPage(null, null, null, "entrega-parcial");
    }

    public static string TextoDestino(string destino) => destino switch
    {
        "NuevaHojaDeRuta" => "Vuelven a salir en otra hoja de ruta",
        "Devolucion" => "Vuelven al comercio",
        _ => destino
    };
}
