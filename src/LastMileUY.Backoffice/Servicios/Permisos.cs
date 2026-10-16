using System.Security.Claims;

namespace LastMileUY.Backoffice.Servicios;

// Qué puede hacer cada perfil en el Backoffice. Los roles vienen de la API (GET /api/auth/me)
// y quedan en la sesión al iniciarla. La API vuelve a controlar cada operación: esto es para
// no mostrar acciones que después la API rechazaría, y para cortar el acceso a páginas enteras.
public static class Permisos
{
    public const string Operador = "Operador";
    public const string Despachador = "Despachador";

    // Políticas de autorización (se registran en Program.cs)
    public const string PoliticaBackoffice = "Backoffice";
    public const string PoliticaConfiguracion = "ConfiguracionDelOperador";

    // Quién entra al Backoffice. El repartidor usa su app y el comercio su portal.
    public static readonly string[] RolesBackoffice = [Operador, Despachador];

    // Tiene que coincidir con la API: PATCH /api/envios/{id}/estado pide Operador o Despachador
    public static readonly string[] RolesCambiarEstado = [Operador, Despachador];

    // Identidad y reglas del operador: solo el perfil Operador (el despachador trabaja con los envíos)
    public static readonly string[] RolesConfiguracion = [Operador];

    // Tiene que coincidir con la API: armar y despachar hojas de ruta (/api/rutas) pide Operador o Despachador
    public static readonly string[] RolesPlanificarRutas = [Operador, Despachador];

    public static bool PuedeEntrar(IEnumerable<string> roles) => roles.Any(RolesBackoffice.Contains);

    public static bool PuedeCambiarEstado(ClaimsPrincipal usuario) => RolesCambiarEstado.Any(usuario.IsInRole);

    public static bool PuedeVerConfiguracion(ClaimsPrincipal usuario) => RolesConfiguracion.Any(usuario.IsInRole);

    public static bool PuedePlanificarRutas(ClaimsPrincipal usuario) => RolesPlanificarRutas.Any(usuario.IsInRole);

    // "Operador", "Despachador" o "Operador y Despachador", para mostrar junto al nombre
    public static string Perfil(ClaimsPrincipal usuario)
    {
        var roles = usuario.FindAll(ClaimTypes.Role).Select(c => c.Value).Where(RolesBackoffice.Contains).ToList();

        return roles.Count switch
        {
            0 => "Sin perfil",
            1 => roles[0],
            _ => string.Join(", ", roles.SkipLast(1)) + " y " + roles[^1]
        };
    }
}
