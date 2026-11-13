using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Rutas;

public class NuevaModel(IRutasApi rutasApi, ILogger<NuevaModel> logger, IOperadorApi? operadorApi = null) : PageModel
{
    public OpcionesRutaDto Opciones { get; private set; } = new([], [], []);

    public bool OrdenSugeridoHabilitado { get; private set; } = true;

    [BindProperty]
    public string Nombre { get; set; } = string.Empty;

    [BindProperty]
    public DateOnly Fecha { get; set; } = Formato.Hoy();

    [BindProperty]
    public int RepartidorId { get; set; }

    [BindProperty]
    public int VehiculoId { get; set; }

    [BindProperty]
    public List<int> Seleccionados { get; set; } = [];

    [BindProperty]
    public Dictionary<int, int?> Orden { get; set; } = [];

    public string? Error { get; private set; }

    public SugerenciaOrdenDto? Sugerencia { get; private set; }

    public IEnumerable<EnvioDisponibleDto> EnviosEnPantalla => Sugerencia is null
        ? Opciones.Envios
        : Opciones.Envios
            .Select((envio, posicion) => (Envio: envio, Posicion: posicion))
            .OrderBy(x => Seleccionados.Contains(x.Envio.Id) ? Orden.GetValueOrDefault(x.Envio.Id) ?? int.MaxValue : int.MaxValue)
            .ThenBy(x => x.Posicion)
            .Select(x => x.Envio);

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

    public async Task<IActionResult> OnPostOrdenarAsync(CancellationToken ct)
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
            Error = "Elegí los envíos que querés ordenar.";
            return Page();
        }

        try
        {
            Sugerencia = await rutasApi.SugerirOrdenAsync(envioIds, ct);
            Orden = Sugerencia.EnvioIds
                .Select((id, posicion) => (Id: id, Numero: posicion + 1))
                .ToDictionary(x => x.Id, x => (int?)x.Numero);
        }
        catch (OperacionRechazadaException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo obtener el orden sugerido");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        return Page();
    }

    private async Task<bool> CargarOpcionesAsync(CancellationToken ct)
    {
        if (operadorApi is not null)
        {
            try
            {
                var operador = await operadorApi.ObtenerActualAsync(ct);
                OrdenSugeridoHabilitado = operador.Tiene(global::LastMileUY.Contracts.Operadores.Funcionalidades.OrdenSugerido);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "No se pudo saber si el orden sugerido está habilitado");
            }
        }

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
