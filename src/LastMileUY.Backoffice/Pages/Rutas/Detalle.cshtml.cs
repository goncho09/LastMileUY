using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Rutas;

// Detalle de una hoja de ruta: repartidor, vehículo, carga, paradas en orden, el despacho
// y lo que llegó desde la app del repartidor
public class DetalleModel(
    IRutasApi rutasApi,
    ISincronizacionApi sincronizacionApi,
    ILogger<DetalleModel> logger) : PageModel
{
    public RutaDetalleDto? Ruta { get; private set; }

    // Lo que mandó el repartidor desde la app para esta ruta (vacío mientras está planificada)
    public IReadOnlyList<OperacionSincronizadaDto> Operaciones { get; private set; } = [];

    public string? Error { get; private set; }

    // Resultado del alta o del despacho (sobrevive a la redirección)
    [TempData]
    public string? Aviso { get; set; }

    [TempData]
    public string? ErrorDespacho { get; set; }

    public async Task OnGetAsync(int id, CancellationToken ct)
    {
        try
        {
            Ruta = await rutasApi.ObtenerAsync(id, ct);

            if (Ruta is not null && Ruta.Estado != EstadosRuta.Planificada)
            {
                Operaciones = (await sincronizacionApi.ListarAsync(
                    new FiltroOperaciones(RutaId: id, TamanioPagina: 100), ct)).Items;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener la hoja de ruta {RutaId}", id);
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
            return;
        }

        if (Ruta is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    // Despachar: después se redirige al detalle (así recargar la página no lo repite)
    public async Task<IActionResult> OnPostDespacharAsync(int id, CancellationToken ct)
    {
        // El botón no se muestra sin permiso, pero el POST se puede armar a mano
        if (!Permisos.PuedePlanificarRutas(User))
        {
            ErrorDespacho = "Tu perfil no puede despachar hojas de ruta.";
            return RedirectToPage(new { id });
        }

        try
        {
            var ruta = await rutasApi.DespacharAsync(id, ct);

            if (ruta is not null)
            {
                Aviso = $"La ruta salió: sus {ruta.Paradas.Count} envíos pasaron a En tránsito y el repartidor ya la puede descargar.";
            }
        }
        catch (OperacionRechazadaException ex)
        {
            ErrorDespacho = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo despachar la hoja de ruta {RutaId}", id);
            ErrorDespacho = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        return RedirectToPage(new { id });
    }

    // Qué parte de la capacidad del vehículo se usa (0 a 100), para la barra de carga
    public static int Porcentaje(decimal usado, decimal capacidad) =>
        capacidad <= 0 ? 100 : (int)Math.Min(100, Math.Round(usado * 100 / capacidad));
}
