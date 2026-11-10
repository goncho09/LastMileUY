using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Operadores;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class EditarModel(
    IGestionOperadoresApi operadoresApi,
    ILogger<EditarModel> logger) : PageModel
{
    [BindProperty]
    public FormularioOperadorModel Formulario { get; set; } = new();

    public int OperadorId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var operador = await operadoresApi.ObtenerAsync(id);
        if (operador is null)
            return NotFound();

        OperadorId = id;
        Formulario = FormularioOperadorModel.Desde(operador);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        OperadorId = id;

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await operadoresApi.EditarAsync(id, Formulario.ARequest());

            TempData["MensajeExito"] = "Operador actualizado correctamente.";
            return RedirectToPage("/Operadores/Index");
        }
        catch (OperacionRechazadaException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error al editar operador {Id}", id);
            ModelState.AddModelError("", "No se pudo actualizar el operador.");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Tiempo agotado al editar operador {Id}", id);
            ModelState.AddModelError("", "La solicitud tardó demasiado.");
        }

        return Page();
    }
}