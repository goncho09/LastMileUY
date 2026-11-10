using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Comercios;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class EditarModel(IGestionComerciosApi api) : PageModel
{
    [BindProperty]
    public FormularioComercioModel Formulario { get; set; } = new();

    public int ComercioId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var comercio = await api.ObtenerAsync(id);
        if (comercio is null) return NotFound();

        ComercioId = id;
        Formulario = FormularioComercioModel.Desde(comercio);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        ComercioId = id;
        if (!ModelState.IsValid) return Page();

        try
        {
            await api.EditarAsync(id, Formulario.ARequest());
            TempData["MensajeExito"] = "Comercio actualizado correctamente.";
            return RedirectToPage("/Comercios/Index");
        }
        catch (OperacionRechazadaException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "No se pudo actualizar el comercio.");
        }

        return Page();
    }
}