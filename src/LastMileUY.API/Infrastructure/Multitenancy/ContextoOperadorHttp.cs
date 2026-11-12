using System.Security.Claims;

namespace LastMileUY.API.Infrastructure.Multitenancy;

// Toma el operador y el comercio del usuario que inició sesión (claims "operador_id" y "comercio_id").
// Nunca de un parámetro del pedido: eso lo podría cambiar cualquiera.
public class ContextoOperadorHttp(IHttpContextAccessor httpContextAccessor) : IContextoOperador
{
    public const string ClaimOperadorId = "operador_id";
    public const string ClaimComercioId = "comercio_id";

    // Lo pone solo la autenticación con clave de prueba (AutenticacionClaveApi)
    public const string ClaimAmbiente = "ambiente";
    public const string AmbientePruebas = "pruebas";

    public int? OperadorId => LeerClaim(ClaimOperadorId);

    public int? ComercioId => LeerClaim(ClaimComercioId);

    public bool EnAmbientePruebas =>
        httpContextAccessor.HttpContext?.User.HasClaim(ClaimAmbiente, AmbientePruebas) == true;

    private int? LeerClaim(string tipo)
    {
        var valor = httpContextAccessor.HttpContext?.User.FindFirstValue(tipo);

        return int.TryParse(valor, out var id) ? id : null;
    }
}
