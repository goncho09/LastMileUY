using LastMileUY.Backoffice.Pages.Shared;
using LastMileUY.Contracts.Administracion;
using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Comercios;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class CrearModel(IGestionComerciosApi api) : PageModel
{
    [BindProperty]
    public FormularioComercioModel Formulario { get; set; } = new();

    [BindProperty]
    public UsuarioInicialFormulario Usuario { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            await api.CrearAsync(new CrearComercioConUsuarioRequest(Formulario.Nombre, Formulario.RazonSocial, Formulario.RUT, Formulario.Email, Formulario.Telefono, Usuario.ARequest()));
            TempData["MensajeExito"] = "Comercio creado correctamente.";
            return RedirectToPage("/Comercios/Index");
        }
        catch (OperacionRechazadaException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "No se pudo conectar con la API.");
        }

        return Page();
    }
}