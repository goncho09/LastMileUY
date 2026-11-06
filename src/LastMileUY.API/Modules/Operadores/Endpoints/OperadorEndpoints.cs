using System.Security.Claims;
using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.API.Modules.Operadores.Endpoints;

public static class OperadorEndpoints
{
    public static IEndpointRouteBuilder MapOperadorEndpoints(
        this IEndpointRouteBuilder app)
    {
        // El operador del usuario logueado: nombre, color, logo y reglas operativas.
        // No recibe Id: cada usuario solo puede ver el suyo.
        app.MapGet("/api/operador", async (
                OperadorActualService servicio,
                IContextoOperador contexto,
                CacheDistribuido cache,
                CancellationToken cancellationToken) =>
            {
                if (contexto.OperadorId is not int operadorId)
                    return Results.Unauthorized();

                // Caché por operador (casi nunca cambia): ver CasosDeCache
                var operador = await cache.ObtenerOCrearAsync(
                    CasosDeCache.ConfiguracionOperador,
                    CasosDeCache.ClaveOperador(operadorId),
                    ct => servicio.ObtenerAsync(ct),
                    cancellationToken);

                return operador is null ? Results.Unauthorized() : Results.Ok(operador);
            })
            .WithTags("Operador")
            .WithName("ObtenerOperadorActual")
            .RequireAuthorization();

        // El depósito de donde salen las rutas (punto de partida del orden sugerido de las paradas).
        // Configuración del operador: solo el perfil Operador
        app.MapPut("/api/operador/deposito", async (
                GuardarDepositoRequest request,
                DepositoOperadorService servicio,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return await servicio.GuardarAsync(request, cancellationToken)
                        ? Results.NoContent()
                        : Results.Forbid();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .WithTags("Operador")
            .WithName("GuardarDepositoOperador")
            .RequireAuthorization(politica => politica.RequireRole("Operador"));

        // Reglas de entrega parcial: el historial de versiones y publicar una nueva.
        // Es configuración del operador: solo el perfil Operador (como "Mi operador" en el Backoffice).
        var reglas = app.MapGroup("/api/operador/reglas-entrega-parcial")
            .WithTags("Operador")
            .RequireAuthorization(politica => politica.RequireRole("Operador"));

        reglas.MapGet("/", async (ReglasEntregaParcialService servicio, CancellationToken cancellationToken) =>
                Results.Ok(await servicio.ListarAsync(cancellationToken)))
            .WithName("ListarReglasEntregaParcial");

        reglas.MapPost("/", async (
                PublicarReglaEntregaParcialRequest request,
                ClaimsPrincipal usuario,
                ReglasEntregaParcialService servicio,
                CancellationToken cancellationToken) =>
            {
                var usuarioId = usuario.FindFirstValue(ClaimTypes.NameIdentifier);

                if (usuarioId is null)
                    return Results.Unauthorized();

                try
                {
                    var regla = await servicio.PublicarAsync(request, usuarioId, cancellationToken);
                    return Results.Created("/api/operador/reglas-entrega-parcial", regla);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .WithName("PublicarReglaEntregaParcial");

        return app;
    }
}
