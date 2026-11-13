using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace LastMileUY.CommercePortal.Servicios;

public class SesionActual(AuthenticationStateProvider estadoAutenticacion)
{
    public async Task<ClaimsPrincipal> UsuarioAsync() =>
        (await estadoAutenticacion.GetAuthenticationStateAsync()).User;

    public async Task<string?> CookieApiAsync() =>
        (await UsuarioAsync()).FindFirst(ClaimsPortal.CookieApi)?.Value;
}
