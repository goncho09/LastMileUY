using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Identidad.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LastMileUY.API.Modules.Identidad.Infrastructure;

// Construye al user cuando inicia sesion
public class UsuarioClaimsPrincipalFactory(
    UserManager<Usuario> userManager,
    RoleManager<Rol> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<Usuario, Rol>(
        userManager,
        roleManager,
        optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(
        Usuario usuario)
    {
        var identity = await base.GenerateClaimsAsync(usuario);

        if (usuario.OperadorId.HasValue)
        {
            identity.AddClaim(new Claim(
                ContextoOperadorHttp.ClaimOperadorId,
                usuario.OperadorId.Value.ToString()));
        }

        return identity;
    }
}