using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.Contracts.Auth;
using Microsoft.AspNetCore.Identity;

namespace LastMileUY.API.Modules.Identidad.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Autenticación");

        group.MapPost("/login", async (
                LoginRequest request,
                SignInManager<Usuario> signInManager,
                UserManager<Usuario> userManager) =>
            {
                var usuario = await userManager.FindByEmailAsync(request.Email);

                if (usuario is null || !usuario.Activo)
                {
                    return Results.Unauthorized();
                }

                var resultado = await signInManager.PasswordSignInAsync(
                    usuario,
                    request.Password,
                    isPersistent: false,
                    lockoutOnFailure: true);

                if (!resultado.Succeeded)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new
                {
                    mensaje = "Sesión iniciada correctamente"
                });
            })
            .AllowAnonymous();
        
        group.MapGet("/me", async (
                HttpContext httpContext,
                UserManager<Usuario> userManager) =>
            {
                var usuario = await userManager.GetUserAsync(httpContext.User);

                if (usuario is null || !usuario.Activo)
                {
                    return Results.Unauthorized();
                }

                var roles = await userManager.GetRolesAsync(usuario);

                var response = new UsuarioAutenticadoResponse
                {
                    Id = usuario.Id,
                    Email = usuario.Email ?? string.Empty,
                    Nombre = usuario.Nombre,
                    Apellido = usuario.Apellido,
                    OperadorId = usuario.OperadorId,
                    ComercioId = usuario.ComercioId,
                    Roles = roles
                };

                return Results.Ok(response);
            })
            .RequireAuthorization();
        
        group.MapPost("/logout", async (
                SignInManager<Usuario> signInManager) =>
            {
                await signInManager.SignOutAsync();

                return Results.Ok(new
                {
                    mensaje = "Sesión cerrada correctamente"
                });
            })
            .RequireAuthorization();

        return app;
    }
}