using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Operadores;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class CrearModel(
    IGestionOperadoresApi operadoresApi,
    ILogger<CrearModel> logger) : PageModel
{
    [BindProperty]
    public FormularioOperadorModel Formulario { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await operadoresApi.CrearAsync(Formulario.ARequest());

            TempData["MensajeExito"] = "Operador creado correctamente.";
            return RedirectToPage("/Operadores/Index");
        }
        catch (OperacionRechazadaException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error al crear operador");
            ModelState.AddModelError("", "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Tiempo agotado al crear operador");
            ModelState.AddModelError("", "La solicitud tardó demasiado.");
        }

        return Page();
    }
}