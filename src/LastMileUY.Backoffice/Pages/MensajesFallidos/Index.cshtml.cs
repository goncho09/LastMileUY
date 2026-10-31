
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.MensajesFallidos;

public class IndexModel(
    IMensajesFallidosApi mensajesApi,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? Hasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public ResumenMensajesFallidosDto? Resumen { get; private set; }

    public PaginaResponse<MensajeFallidoResponse>? Resultado
        { get; private set; }

    public string? Error { get; private set; }

    public string? ErrorResumen { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Pagina < 1)
        {
            Pagina = 1;
        }

        // Resumen de la cola RabbitMQ
        try
        {
            Resumen = await mensajesApi.ObtenerResumenAsync(ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or InvalidOperationException)
        {
            logger.LogError(ex,
                "No se pudo consultar el resumen de mensajes fallidos");

            ErrorResumen = "No se pudo consultar el estado de la cola.";
        }

        // Listado de incidencias almacenadas en PostgreSQL
        try
        {
            var filtro = new FiltroMensajesFallidos
            {
                Pagina = Pagina,
                TamanoPagina = 20,
                Estado = Estado,
                Desde = Desde,
                Hasta = Hasta
            };

            Resultado = await mensajesApi.ListarAsync(filtro, ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or InvalidOperationException)
        {
            logger.LogError(ex,
                "No se pudo consultar el listado de mensajes fallidos");

            Error = "No se pudo obtener el listado de mensajes fallidos.";
        }
    }

    // Conserva los filtros al cambiar de página
    public Dictionary<string, string?> RutaPagina(int pagina) => new()
    {
        ["estado"] = Estado,
        ["desde"] = Desde?.ToString("yyyy-MM-dd"),
        ["hasta"] = Hasta?.ToString("yyyy-MM-dd"),
        ["pagina"] = pagina.ToString()
    };
}
