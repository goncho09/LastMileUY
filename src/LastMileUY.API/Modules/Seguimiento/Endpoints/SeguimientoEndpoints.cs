using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Modules.Seguimiento.Application;
using LastMileUY.Contracts.Seguimiento;

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

        app.MapPost("/api/seguimiento/{codigo}/reprogramacion", async (
                string codigo,
                SolicitarReprogramacionRequest request,
                SeguimientoPublicoService seguimiento,
                ReprogramacionPublicaService reprogramaciones,
                CacheDistribuido cache,
                CancellationToken cancellationToken) =>
            {
                codigo = codigo.Trim().ToUpperInvariant();

                if (await seguimiento.UbicarAsync(codigo, cancellationToken) is not { } envio)
                    return Results.NotFound();

                var error = await reprogramaciones.SolicitarAsync(envio.OperadorId, envio.Id, request, cancellationToken);
                await cache.InvalidarAsync(CasosDeCache.ClaveSeguimiento(codigo), cancellationToken);

                return error is null
                    ? Results.NoContent()
                    : Results.Conflict(new { mensaje = error });
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaLimite)
            .WithTags("Seguimiento")
            .WithName("SolicitarReprogramacion");

        return app;
    }
}
