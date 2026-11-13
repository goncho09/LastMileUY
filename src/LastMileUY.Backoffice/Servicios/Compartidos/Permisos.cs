using System.Security.Claims;

namespace LastMileUY.Backoffice.Servicios;

public static class Permisos
{
    public const string Operador = "Operador";
    public const string Despachador = "Despachador";
    public const string Administrador = "Administrador";

    public const string PoliticaBackoffice = "Backoffice";
    public const string PoliticaConfiguracion = "ConfiguracionDelOperador";
    public const string PoliticaAdministracionGlobal = "AdministracionGlobal";
    public const string PoliticaOperacion = "OperacionLogistica";

    public static readonly string[] RolesBackoffice = [Administrador, Operador, Despachador];

    public static readonly string[] RolesAdministracionGlobal =
        [Administrador];

    public static readonly string[] RolesOperacion =
        [Operador, Despachador];

    public static bool PuedeAdministrarMensajeria(ClaimsPrincipal usuario) =>
        usuario.IsInRole(Administrador);

    public static readonly string[] RolesCambiarEstado = [Operador, Despachador];

    public static readonly string[] RolesConfiguracion = [Operador];

    public static readonly string[] RolesPlanificarRutas = [Operador, Despachador];

    public static bool PuedeEntrar(IEnumerable<string> roles) => roles.Any(RolesBackoffice.Contains);

    public static bool PuedeCambiarEstado(ClaimsPrincipal usuario) => RolesCambiarEstado.Any(usuario.IsInRole);

    public static bool PuedeVerConfiguracion(ClaimsPrincipal usuario) => RolesConfiguracion.Any(usuario.IsInRole);

    public static bool PuedePlanificarRutas(ClaimsPrincipal usuario) => RolesPlanificarRutas.Any(usuario.IsInRole);

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
