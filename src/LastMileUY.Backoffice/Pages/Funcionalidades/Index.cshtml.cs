using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Operadores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Funcionalidades;

public class IndexModel(IFuncionalidadesApi funcionalidadesApi, ILogger<IndexModel> logger) : PageModel
{
    public IReadOnlyList<FuncionalidadesOperadorDto> Operadores { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await CargarAsync(ct);

    public async Task<IActionResult> OnPostCambiarAsync(int operadorId, string clave, bool activa, CancellationToken ct)
    {
        try
        {
            await funcionalidadesApi.CambiarAsync(operadorId, clave, activa, ct);

            var nombre = Contracts.Operadores.Funcionalidades.Buscar(clave)?.Nombre ?? clave;
            TempData["Aviso"] = $"\"{nombre}\" quedó {(activa ? "prendida" : "apagada")}.";

            return RedirectToPage();
        }
        catch (OperacionRechazadaException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo cambiar la funcionalidad {Clave} del operador {OperadorId}", clave, operadorId);
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        await CargarAsync(ct);
        return Page();
    }

    private async Task CargarAsync(CancellationToken ct)
    {
        try
        {
            Operadores = await funcionalidadesApi.ListarAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudieron cargar las funcionalidades");
            Error ??= "No se pudieron cargar las funcionalidades. Probá de nuevo en unos minutos.";
        }
    }
}
