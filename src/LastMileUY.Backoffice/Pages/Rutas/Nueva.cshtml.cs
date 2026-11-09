using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Rutas;

// Armar una hoja de ruta: nombre, fecha, repartidor, vehículo y los envíos en el orden de visita.
// Las reglas (capacidad, franjas, duplicados) las controla la API; acá se muestra su respuesta.
// "Ordenar por recorrido" le pide a la API un orden sugerido y lo carga en los números de orden:
// el despachador lo revisa, lo cambia si quiere y recién ahí arma la ruta.
// Si el administrador le apagó al operador el orden sugerido (docs/funcionalidades.md), el botón no aparece.
public class NuevaModel(IRutasApi rutasApi, ILogger<NuevaModel> logger, IOperadorApi? operadorApi = null) : PageModel
{
    public OpcionesRutaDto Opciones { get; private set; } = new([], [], []);

    // "Ordenar por recorrido" habilitado para el operador. Si no se puede saber, se muestra: la API
    // igual lo rechaza con el motivo si está apagado
    public bool OrdenSugeridoHabilitado { get; private set; } = true;

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

    // El último orden sugerido (null si no se pidió)
    public SugerenciaOrdenDto? Sugerencia { get; private set; }

    // Los envíos como se muestran: después de ordenar, primero los elegidos en el orden sugerido
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

    // Pide el orden sugerido para los envíos tildados y lo carga en los números de orden (no arma la ruta)
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
