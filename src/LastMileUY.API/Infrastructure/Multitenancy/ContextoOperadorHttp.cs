using System.Security.Claims;

namespace LastMileUY.API.Infrastructure.Multitenancy;

// Toma el operador del usuario que inició sesión (claim "operador_id").
// Nunca de un parámetro del pedido: eso lo podría cambiar cualquiera.
public class ContextoOperadorHttp(IHttpContextAccessor httpContextAccessor) : IContextoOperador
{
    public const string ClaimOperadorId = "operador_id";

    public int? OperadorId
    {
        get
        {
            var valor = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimOperadorId);

            return int.TryParse(valor, out var operadorId) ? operadorId : null;
        }
    }
}
