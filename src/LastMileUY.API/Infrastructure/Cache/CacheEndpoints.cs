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
                IReadOnlyDictionary<string, (long Aciertos, long Fallos)> numeros;

                try
                {
                    numeros = await metricas.ObtenerAsync(cancellationToken);
                }
                catch (MetricasCacheNoDisponiblesException)
                {
                    return RedisNoDisponible();
                }

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
                try
                {
                    await metricas.ReiniciarAsync(cancellationToken);
                }
                catch (MetricasCacheNoDisponiblesException)
                {
                    return RedisNoDisponible();
                }

                return Results.NoContent();
            })
            .RequireAuthorization(politica => politica.RequireRole("Administrador"))
            .WithName("ReiniciarMetricasCache");

        return app;
    }

    // 503 y no 500: no es un error de la API, es Redis que no responde (el Backoffice muestra el aviso)
    private static IResult RedisNoDisponible() =>
        Results.Problem(
            title: "Redis no disponible",
            detail: "Los contadores del caché se guardan en Redis y no responde. La API sigue atendiendo: " +
                    "mientras tanto las consultas van directo a la base.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
