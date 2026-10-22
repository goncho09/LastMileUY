using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Operadores.Application;

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

        return app;
    }
}
