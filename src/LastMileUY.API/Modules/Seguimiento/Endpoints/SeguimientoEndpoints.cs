using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Modules.Seguimiento.Application;

namespace LastMileUY.API.Modules.Seguimiento.Endpoints;

public static class SeguimientoEndpoints
{
    public const string PoliticaLimite = "seguimiento-publico";

    public static IEndpointRouteBuilder MapSeguimientoEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seguimiento/{codigo}", async (
                string codigo,
                SeguimientoPublicoService seguimiento,
                CacheDistribuido cache,
                CancellationToken cancellationToken) =>
            {
                codigo = codigo.Trim().ToUpperInvariant();

                var envio = await cache.ObtenerOCrearAsync(
                    CasosDeCache.SeguimientoPublico,
                    CasosDeCache.ClaveSeguimiento(codigo),
                    ct => seguimiento.ObtenerAsync(codigo, ct),
                    cancellationToken);

                return envio is null
                    ? Results.NotFound()
                    : Results.Ok(envio);
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaLimite)
            .WithTags("Seguimiento")
            .WithName("SeguimientoPublico");

        return app;
    }
}
