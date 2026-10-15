using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages;

// Inicio de sesión: valida contra la API y guarda la sesión en la cookie del Backoffice
[AllowAnonymous]
public class LoginModel(IAutenticacionApi autenticacion, ILogger<LoginModel> logger) : PageModel
{
    public const string ClaimOperadorId = "operador_id";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá tu email.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Volver();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        SesionIniciada? sesion;

        try
        {
            sesion = await autenticacion.IniciarSesionAsync(Email.Trim(), Password, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "No se pudo iniciar sesión contra la API");
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
            return Page();
        }

        if (sesion is null)
        {
            Error = "Email o contraseña incorrectos.";
            return Page();
        }

        // El Backoffice es del operador: sin operador no vería ningún envío
        if (sesion.OperadorId is null)
        {
            Error = "Este usuario no pertenece a ningún operador.";
            return Page();
        }

        // Un repartidor o un comercio tienen su propia aplicación
        var roles = sesion.Roles ?? [];

        if (!Permisos.PuedeEntrar(roles))
        {
            Error = "Tu perfil no tiene acceso al Backoffice.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, sesion.Nombre),
            new(ClaimTypes.Email, sesion.Email),
            new(ClaimOperadorId, sesion.OperadorId.Value.ToString())
        };

        claims.AddRange(roles.Select(rol => new Claim(ClaimTypes.Role, rol)));

        if (sesion.CookieApi is not null)
        {
            claims.Add(new Claim(ReenviarCookieApi.ClaimCookieApi, sesion.CookieApi));
        }

        // Identidad visual del operador (ver IdentidadOperador y _Layout)
        if (sesion.Operador is not null)
        {
            claims.Add(new Claim(IdentidadOperador.ClaimNombre, sesion.Operador.Nombre));

            if (sesion.Operador.ColorPrimario is not null)
            {
                claims.Add(new Claim(IdentidadOperador.ClaimColor, sesion.Operador.ColorPrimario));
            }

            if (sesion.Operador.LogoUrl is not null)
            {
                claims.Add(new Claim(IdentidadOperador.ClaimLogo, sesion.Operador.LogoUrl));
            }
        }

        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad));

        return Volver();
    }

    // Solo vuelve a direcciones del propio Backoffice (evita redirigir a otro sitio)
    private IActionResult Volver() =>
        Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Index");
}
