using LastMileUY.Backoffice.Servicios;
using System.Globalization;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Operador;

// Identidad y configuración del operador logueado: se pide a la API en cada visita,
// así muestra los datos al día (el color del encabezado se toma al iniciar sesión).
// También las reglas de entrega parcial: el historial de versiones y el formulario para publicar una nueva.
// Y el depósito de donde salen las rutas: el punto de partida del orden sugerido de las paradas.
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

    [TempData]
    public string? AvisoDeposito { get; set; }

    [TempData]
    public string? ErrorDeposito { get; set; }

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
        bool permiteEntregaParcial,
        string? destinoBultosPendientes,
        decimal recargoNuevaVisita,
        string? motivo,
        CancellationToken ct)
    {
        try
        {
            var regla = await operadorApi.PublicarReglaEntregaParcialAsync(
                new PublicarReglaEntregaParcialRequest
                {
                    PermiteEntregaParcial = permiteEntregaParcial,
                    DestinoBultosPendientes = destinoBultosPendientes ?? string.Empty,
                    RecargoNuevaVisita = recargoNuevaVisita,
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

    // Guardar (o borrar, con borrar = true) el depósito. Los números se leen con punto o coma decimal,
    // como se copian de un mapa, sin depender del idioma del navegador
    public async Task<IActionResult> OnPostGuardarDepositoAsync(
        string? latitud, string? longitud, bool borrar, CancellationToken ct)
    {
        Coordenadas? deposito = null;

        if (!borrar)
        {
            if (!LeerGrados(latitud, out var lat) || !LeerGrados(longitud, out var lon))
            {
                ErrorDeposito = "Escribí la latitud y la longitud en grados decimales, por ejemplo -34.9011 y -56.1645.";
                return RedirectToPage(null, null, null, "deposito");
            }

            deposito = new Coordenadas(lat, lon);
        }

        try
        {
            await operadorApi.GuardarDepositoAsync(deposito, ct);
            AvisoDeposito = deposito is null
                ? "Se borró el depósito. El orden sugerido va a arrancar por la primera parada."
                : "Se guardó el depósito. El orden sugerido de las paradas arranca desde ahí.";
        }
        catch (OperacionRechazadaException ex)
        {
            ErrorDeposito = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo guardar el depósito");
            ErrorDeposito = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        return RedirectToPage(null, null, null, "deposito");
    }

    public static bool LeerGrados(string? texto, out decimal grados) =>
        decimal.TryParse(
            texto?.Trim().Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out grados);

    public static string Grados(decimal grados) => grados.ToString("0.######", CultureInfo.InvariantCulture);

    public static string TextoDestino(string destino) => destino switch
    {
        "NuevaHojaDeRuta" => "Vuelven a salir en otra hoja de ruta",
        "Devolucion" => "Vuelven al comercio",
        _ => destino
    };
}
