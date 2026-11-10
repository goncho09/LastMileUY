
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comercios;
using LastMileUY.Contracts.Operadores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Comercios;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class OperadoresModel(
    IGestionComerciosApi comerciosApi,
    IGestionOperadoresApi operadoresApi) : PageModel
{
    public ComercioAdminDto Comercio { get; private set; } = null!;

    public List<VinculoOperadorComercioDto> Vinculos { get; private set; } = [];

    public List<OperadorAdminDto> Disponibles { get; private set; } = [];

    [BindProperty]
    public int OperadorId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var comercio = await comerciosApi.ObtenerAsync(id);

        if (comercio is null)
            return NotFound();

        Comercio = comercio;

        Vinculos = await comerciosApi.ListarVinculosAsync(id);

        var operadores = await operadoresApi.ListarAsync();

        Disponibles = operadores
            .Where(o => o.Activo &&
                !Vinculos.Any(v =>
                    v.OperadorId == o.Id && v.Activo))
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostVincularAsync(int id)
    {
        if (OperadorId <= 0)
        {
            TempData["Error"] = "Seleccioná un operador.";
            return RedirectToPage(new { id });
        }

        try
        {
            await comerciosApi.VincularAsync(id, OperadorId);
            TempData["MensajeExito"] = "Operador vinculado correctamente.";
        }
        catch (OperacionRechazadaException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (HttpRequestException)
        {
            TempData["Error"] = "No se pudo vincular el operador.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDesvincularAsync(
        int id, int operadorId)
    {
        try
        {
            await comerciosApi.DesvincularAsync(id, operadorId);
            TempData["MensajeExito"] = "Operador desvinculado correctamente.";
        }
        catch (OperacionRechazadaException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (HttpRequestException)
        {
            TempData["Error"] = "No se pudo desvincular el operador.";
        }

        return RedirectToPage(new { id });
    }
}
