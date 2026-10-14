using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace LastMileUY.CommercePortal.Servicios;

// Datos del usuario logueado, leídos desde el AuthenticationStateProvider.
// En las páginas interactivas de Blazor Server no hay HttpContext confiable (corren sobre el circuito
// de SignalR), por eso no se usa IHttpContextAccessor como en el Backoffice: el estado de autenticación
// del circuito sí está disponible siempre.
public class SesionActual(AuthenticationStateProvider estadoAutenticacion)
{
    public async Task<ClaimsPrincipal> UsuarioAsync() =>
        (await estadoAutenticacion.GetAuthenticationStateAsync()).User;

    public async Task<string?> CookieApiAsync() =>
        (await UsuarioAsync()).FindFirst(ClaimsPortal.CookieApi)?.Value;
}
