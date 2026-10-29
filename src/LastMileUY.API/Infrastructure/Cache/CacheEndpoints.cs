using LastMileUY.Contracts.Cache;

namespace LastMileUY.API.Infrastructure.Cache;

// Tablero técnico del caché: aciertos, fallos y porcentaje de aciertos de cada caso.
// Verlas: el perfil Operador (el mismo que ve la configuración del operador en el Backoffice).
// Reiniciarlas: solo el Administrador. Son de toda la plataforma: si las reiniciara un operador,
// les borraría la medición a los demás.
public static class CacheEndpoints
{
    public static IEndpointRouteBuilder MapCacheEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cache")
            .WithTags("Caché")
            .RequireAuthorization();

        group.MapGet("/metricas", async (IMetricasCache metricas, CancellationToken cancellationToken) =>
            {
                var numeros = await metricas.ObtenerAsync(cancellationToken);

                return Results.Ok(CasosDeCache.Todos.Select(caso =>
                {
                    var (aciertos, fallos) = numeros[caso.Id];
                    var total = aciertos + fallos;

                    return new MetricaCacheDto(
                        caso.Id,
                        caso.Nombre,
                        caso.Perfil,
                        (int)caso.Duracion.TotalSeconds,
                        aciertos,
                        fallos,
                        total == 0 ? 0 : Math.Round(aciertos * 100.0 / total, 1));
                }).ToList());
            })
            .RequireAuthorization(politica => politica.RequireRole("Operador"))
            .WithName("MetricasCache");

        // Para empezar una medición de cero (por ejemplo antes de una demostración)
        group.MapPost("/metricas/reiniciar", async (IMetricasCache metricas, CancellationToken cancellationToken) =>
            {
                await metricas.ReiniciarAsync(cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization(politica => politica.RequireRole("Administrador"))
            .WithName("ReiniciarMetricasCache");

        return app;
    }
}
