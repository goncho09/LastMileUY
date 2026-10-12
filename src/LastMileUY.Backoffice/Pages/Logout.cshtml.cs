using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages;

// Cerrar sesión: solo por POST (el botón del menú), para que un enlace no pueda desloguear a nadie
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Envios/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToPage("/Login");
    }
}
