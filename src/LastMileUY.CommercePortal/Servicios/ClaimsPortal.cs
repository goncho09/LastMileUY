using System.Security.Claims;

namespace LastMileUY.CommercePortal.Servicios;

// Qué guarda la cookie (cifrada) del portal sobre el usuario logueado.
// Adentro va también la cookie de la API, que se reenvía en cada llamada (igual que el Backoffice).
public static class ClaimsPortal
{
    public const string CookieApi = "cookie_api";
    public const string ComercioId = "comercio_id";
    public const string OperadorId = "operador_id";
    public const string OperadorNombre = "operador_nombre";

    public static ClaimsPrincipal CrearPrincipal(SesionApi sesion, string esquema)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, sesion.Nombre),
            new(ClaimTypes.Email, sesion.Email),
            new(ComercioId, sesion.ComercioId.ToString()),
            new(OperadorId, sesion.OperadorId.ToString()),
            new(OperadorNombre, sesion.OperadorNombre ?? $"Operador {sesion.OperadorId}"),
            new(CookieApi, sesion.CookieApi)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, esquema));
    }

    public static int? LeerEntero(this ClaimsPrincipal usuario, string tipo) =>
        int.TryParse(usuario.FindFirst(tipo)?.Value, out var valor) ? valor : null;
}
