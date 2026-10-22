using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Modules.Seguimiento.Application;

namespace LastMileUY.API.Modules.Seguimiento.Endpoints;

public static class SeguimientoEndpoints
{
    // Límite de consultas por IP (se configura en Program.cs)
    public const string PoliticaLimite = "seguimiento-publico";

    public static IEndpointRouteBuilder MapSeguimientoEndpoints(
        this IEndpointRouteBuilder app)
    {
        // Público: lo usa la página de seguimiento (PublicTracking). 404 tanto si el código
        // no existe como si está mal escrito, para no dar pistas a quien prueba códigos.
        app.MapGet("/api/seguimiento/{codigo}", async (
                string codigo,
                SeguimientoPublicoService seguimiento,
                CacheDistribuido cache,
                CancellationToken cancellationToken) =>
            {
                // Mismo formato que guarda la base, así "abc123" y "ABC123 " usan la misma entrada del caché
                codigo = codigo.Trim().ToUpperInvariant();

                // Caché (ver CasosDeCache): solo se guardan envíos encontrados, nunca un "no existe"
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
