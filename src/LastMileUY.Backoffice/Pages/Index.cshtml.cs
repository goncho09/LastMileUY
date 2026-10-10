using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages;

// Por ahora la pantalla principal del Backoffice es el listado de envíos
public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Envios/Index");
}
