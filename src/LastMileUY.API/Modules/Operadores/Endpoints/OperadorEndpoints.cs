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
                CancellationToken cancellationToken) =>
            {
                var operador = await servicio.ObtenerAsync(cancellationToken);

                return operador is null ? Results.Unauthorized() : Results.Ok(operador);
            })
            .WithTags("Operador")
            .WithName("ObtenerOperadorActual")
            .RequireAuthorization();

        return app;
    }
}
