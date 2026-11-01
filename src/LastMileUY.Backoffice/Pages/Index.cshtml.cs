
using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages;

[Authorize]
public class IndexModel(
    IEnviosApi enviosApi,
    ILogger<IndexModel> logger) : PageModel
{
    public int TotalEnvios { get; private set; }
    public int EnTransito { get; private set; }
    public int Entregados { get; private set; }
    public int Incidencias { get; private set; }

    public string? Error { get; private set; }
    
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // Los administradores van directamente a Mensajes fallidos
        if (User.IsInRole("Administrador"))
        {
            return RedirectToPage("/MensajesFallidos/Index");
        }

        // Los demás usuarios siguen viendo el inicio normal
        try
        {
            var datos = await ObtenerEstadisticasAsync(ct);

            TotalEnvios = datos.TotalEnvios;
            EnTransito = datos.EnTransito;
            Entregados = datos.Entregados;
            Incidencias = datos.Incidencias;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error cargando estadísticas del Backoffice");

            Error = "No se pudieron cargar las estadísticas.";
        }

        return Page();
    }


    public async Task<IActionResult> OnGetEstadisticasAsync(
        CancellationToken ct)
    {
        try
        {
            var datos = await ObtenerEstadisticasAsync(ct);

            Response.Headers.CacheControl = "no-store";

            return new JsonResult(datos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error actualizando estadísticas del Backoffice");

            return StatusCode(503);
        }
    }

    private async Task<EstadisticasTablero> ObtenerEstadisticasAsync(
        CancellationToken ct)
    {
        const int tamanioPagina = 1;

        var total = await enviosApi.ListarAsync(
            new FiltroEnvios(TamanioPagina: tamanioPagina), ct);

        var transito = await enviosApi.ListarAsync(
            new FiltroEnvios(
                Estado: "EnTransito",
                TamanioPagina: tamanioPagina), ct);

        var entregados = await enviosApi.ListarAsync(
            new FiltroEnvios(
                Estado: "Entregado",
                TamanioPagina: tamanioPagina), ct);

        var noEntregados = await enviosApi.ListarAsync(
            new FiltroEnvios(
                Estado: "NoEntregado",
                TamanioPagina: tamanioPagina), ct);

        var extraviados = await enviosApi.ListarAsync(
            new FiltroEnvios(
                Estado: "Extraviado",
                TamanioPagina: tamanioPagina), ct);

        return new EstadisticasTablero(
            total.Total,
            transito.Total,
            entregados.Total,
            noEntregados.Total + extraviados.Total);
    }
    
    
    public async Task<IActionResult> OnGetActividadAsync(
        CancellationToken ct)
    {
        try
        {
            var eventos = await enviosApi.ObtenerActividadRecienteAsync(ct);

            Response.Headers.CacheControl = "no-store";

            return new JsonResult(eventos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error obteniendo actividad reciente del Backoffice");

            return StatusCode(503);
        }
    }


    public record EstadisticasTablero(
        int TotalEnvios,
        int EnTransito,
        int Entregados,
        int Incidencias);
}
