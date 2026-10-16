using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Rutas;

// Armar una hoja de ruta: nombre, fecha, repartidor, vehículo y los envíos en el orden de visita.
// Las reglas (capacidad, franjas, duplicados) las controla la API; acá se muestra su respuesta.
public class NuevaModel(IRutasApi rutasApi, ILogger<NuevaModel> logger) : PageModel
{
    public OpcionesRutaDto Opciones { get; private set; } = new([], [], []);

    [BindProperty]
    public string Nombre { get; set; } = string.Empty;

    [BindProperty]
    public DateOnly Fecha { get; set; } = Formato.Hoy();

    [BindProperty]
    public int RepartidorId { get; set; }

    [BindProperty]
    public int VehiculoId { get; set; }

    // Los envíos tildados
    [BindProperty]
    public List<int> Seleccionados { get; set; } = [];

    // El número de orden que se le puso a cada envío (opcional)
    [BindProperty]
    public Dictionary<int, int?> Orden { get; set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Permisos.PuedePlanificarRutas(User))
        {
            return Forbid();
        }

        await CargarOpcionesAsync(ct);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!Permisos.PuedePlanificarRutas(User))
        {
            return Forbid();
        }

        if (!await CargarOpcionesAsync(ct))
        {
            return Page();
        }

        var envioIds = ArmadoDeRuta.OrdenarSeleccion(
            Opciones.Envios.Select(e => e.Id).ToList(), Seleccionados, Orden);

        if (envioIds.Count == 0)
        {
            Error = "Elegí al menos un envío.";
            return Page();
        }

        try
        {
            var ruta = await rutasApi.CrearAsync(new CrearRutaRequest
            {
                Nombre = Nombre,
                Fecha = Fecha,
                RepartidorId = RepartidorId,
                VehiculoId = VehiculoId,
                EnvioIds = envioIds
            }, ct);

            TempData["Aviso"] = $"Se armó la hoja de ruta \"{ruta.Nombre}\" con {ruta.Paradas.Count} paradas.";

            return RedirectToPage("/Rutas/Detalle", new { id = ruta.Id });
        }
        catch (OperacionRechazadaException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo crear la hoja de ruta");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        return Page();
    }

    private async Task<bool> CargarOpcionesAsync(CancellationToken ct)
    {
        try
        {
            Opciones = await rutasApi.ObtenerOpcionesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudieron obtener las opciones para armar una hoja de ruta");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
            return false;
        }
    }
}
