using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Identidad.Application;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.Contracts.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Identidad.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Autenticación");

        // Usuario del operador: su operador sale de la base (claim operador_id).
        // Usuario de comercio: no tiene operador propio (CK_Usuarios_Ambito). Elige con cuál
        // trabaja en esta sesión; se valida contra OperadoresComercios y se guarda solo en la cookie.
        group.MapPost("/login", async (
                LoginRequest request,
                SignInManager<Usuario> signInManager,
                UserManager<Usuario> userManager,
                OperadoresDeComercioService operadoresDeComercio,
                SeleccionOperadorTemporalService seleccionTemporal,
                CancellationToken cancellationToken) =>
            {
                var usuario = await userManager.FindByEmailAsync(request.Email);

                if (usuario is null || !usuario.Activo)
                {
                    return Results.Unauthorized();
                }

                // Verifica la contraseña (con bloqueo por intentos) sin iniciar la sesión todavía
                var resultado = await signInManager.CheckPasswordSignInAsync(
                    usuario,
                    request.Password,
                    lockoutOnFailure: true);

                if (!resultado.Succeeded)
                {
                    return Results.Unauthorized();
                }

                if (usuario.ComercioId is null)
                {
                    await signInManager.SignInAsync(usuario, isPersistent: false);
                    return SesionIniciada();
                }

                var operadores = await operadoresDeComercio.ListarAsync(
                    usuario.ComercioId.Value, cancellationToken);

                if (operadores.Count == 0)
                {
                    return Results.Json(
                        new { mensaje = "Tu comercio no trabaja con ningún operador activo." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                int operadorId;

                if (request.OperadorId is int elegido)
                {
                    if (operadores.All(o => o.Id != elegido))
                    {
                        return Results.BadRequest(new
                        {
                            mensaje = "Tu comercio no trabaja con ese operador."
                        });
                    }

                    operadorId = elegido;
                }
                else if (operadores.Count == 1)
                {
                    operadorId = operadores[0].Id;
                }
                else
                {
                    var tokenTemporal = seleccionTemporal.Crear(
                        await userManager.GetUserIdAsync(usuario));

                    return Results.Conflict(new
                    {
                        Mensaje = "Elegí con qué operador vas a trabajar.",
                        Operadores = operadores,
                        TokenTemporal = tokenTemporal
                    });
                }

                await IniciarSesionConOperadorAsync(signInManager, usuario, operadorId);
                return SesionIniciada();
            })
            .AllowAnonymous();
        
        
        group.MapPost("/seleccionar-operador", async (
                SeleccionarOperadorRequest request,
                SeleccionOperadorTemporalService seleccionTemporal,
                UserManager<Usuario> userManager,
                SignInManager<Usuario> signInManager,
                OperadoresDeComercioService operadoresDeComercio,
                CancellationToken cancellationToken) =>
            {
                if (!seleccionTemporal.IntentarConsumir(
                        request.TokenTemporal, out var usuarioId))
                {
                    return Results.Unauthorized();
                }

                var usuario = await userManager.FindByIdAsync(usuarioId);

                if (usuario is null || !usuario.Activo || usuario.ComercioId is null)
                {
                    return Results.Unauthorized();
                }

                var operadores = await operadoresDeComercio.ListarAsync(
                    usuario.ComercioId.Value, cancellationToken);

                if (operadores.All(o => o.Id != request.OperadorId))
                {
                    return Results.BadRequest(new
                    {
                        mensaje = "Tu comercio no trabaja con ese operador."
                    });
                }

                await IniciarSesionConOperadorAsync(
                    signInManager, usuario, request.OperadorId);

                return SesionIniciada();
            })
            .AllowAnonymous();


        group.MapGet("/me", async (
                HttpContext httpContext,
                UserManager<Usuario> userManager,
                IContextoOperador contextoOperador,
                ApplicationDbContext db,
                CancellationToken cancellationToken) =>
            {
                var usuario = await userManager.GetUserAsync(httpContext.User);

                if (usuario is null || !usuario.Activo)
                {
                    return Results.Unauthorized();
                }

                var roles = await userManager.GetRolesAsync(usuario);

                // El operador de la sesión: el del usuario o, si es de comercio, el que eligió al entrar
                var operadorId = contextoOperador.OperadorId;

                var operadorNombre = operadorId is null
                    ? null
                    : await db.Operadores
                        .Where(o => o.Id == operadorId)
                        .Select(o => o.Nombre)
                        .FirstOrDefaultAsync(cancellationToken);

                var response = new UsuarioAutenticadoResponse
                {
                    Id = usuario.Id,
                    Email = usuario.Email ?? string.Empty,
                    Nombre = usuario.Nombre,
                    Apellido = usuario.Apellido,
                    OperadorId = operadorId,
                    OperadorNombre = operadorNombre,
                    ComercioId = usuario.ComercioId,
                    Roles = roles
                };

                return Results.Ok(response);
            })
            .RequireAuthorization();

        // Operadores con los que trabaja el comercio del usuario (para "Cambiar operador")
        group.MapGet("/operadores", async (
                IContextoOperador contextoOperador,
                OperadoresDeComercioService operadoresDeComercio,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.ComercioId is not int comercioId)
                {
                    return Results.Forbid();
                }

                return Results.Ok(await operadoresDeComercio.ListarAsync(comercioId, cancellationToken));
            })
            .RequireAuthorization();

        // El usuario de comercio pasa a trabajar con otro de sus operadores (se renueva la cookie)
        group.MapPost("/operador", async (
                CambiarOperadorRequest request,
                HttpContext httpContext,
                SignInManager<Usuario> signInManager,
                UserManager<Usuario> userManager,
                OperadoresDeComercioService operadoresDeComercio,
                CancellationToken cancellationToken) =>
            {
                var usuario = await userManager.GetUserAsync(httpContext.User);

                if (usuario is null || !usuario.Activo)
                {
                    return Results.Unauthorized();
                }

                if (usuario.ComercioId is null)
                {
                    return Results.BadRequest(new
                    {
                        mensaje = "Solo un usuario de comercio puede cambiar de operador."
                    });
                }

                var operadores = await operadoresDeComercio.ListarAsync(
                    usuario.ComercioId.Value, cancellationToken);

                if (operadores.All(o => o.Id != request.OperadorId))
                {
                    return Results.BadRequest(new
                    {
                        mensaje = "Tu comercio no trabaja con ese operador."
                    });
                }

                await IniciarSesionConOperadorAsync(signInManager, usuario, request.OperadorId);
                return SesionIniciada();
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

    // Inicia la sesión agregando el operador elegido como claim (solo vive en la cookie, no en la base)
    private static Task IniciarSesionConOperadorAsync(
        SignInManager<Usuario> signInManager,
        Usuario usuario,
        int operadorId) =>
        signInManager.SignInWithClaimsAsync(
            usuario,
            isPersistent: false,
            [new Claim(ContextoOperadorHttp.ClaimOperadorId, operadorId.ToString())]);

    private static IResult SesionIniciada() =>
        Results.Ok(new
        {
            mensaje = "Sesión iniciada correctamente"
        });
}
