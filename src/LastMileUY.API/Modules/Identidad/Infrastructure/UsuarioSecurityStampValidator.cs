using System.Security.Claims;
using LastMileUY.API.Modules.Identidad.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LastMileUY.API.Modules.Identidad.Infrastructure;

public class UsuarioSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<Usuario> signInManager,
    ILoggerFactory loggerFactory)
    : SecurityStampValidator<Usuario>(
        options,
        signInManager,
        loggerFactory)
{
    protected override async Task<Usuario?> VerifySecurityStamp(
        ClaimsPrincipal? principal)
    {
        var usuario = await base.VerifySecurityStamp(principal);

        return usuario is { Activo: true } ? usuario : null;
    }
}