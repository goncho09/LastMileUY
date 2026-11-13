using LastMileUY.API.Modules.Notificaciones.Application;

namespace LastMileUY.API.Modules.Notificaciones.Endpoints;

public static class AvisosEndpoints
{
    public static IEndpointRouteBuilder MapAvisosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/avisos")
            .WithTags("Avisos")
            .RequireAuthorization(politica => politica.RequireRole("Comercio"));

        group.MapGet("/", async (
                AvisosComercioService servicio,
                CancellationToken ct,
                int pagina = 1,
                int tamanioPagina = 20,
                string? estado = null) =>
            {
                try
                {
                    return Results.Ok(await servicio.ListarAsync(pagina, tamanioPagina, estado, ct));
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
            .WithName("ListarAvisos");

        group.MapGet("/{id:int}", async (int id, AvisosComercioService servicio, CancellationToken ct) =>
            {
                try
                {
                    var aviso = await servicio.ObtenerAsync(id, ct);
                    return aviso is null ? Results.NotFound() : Results.Ok(aviso);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .WithName("ObtenerAviso");

        group.MapPost("/{id:int}/reenviar", async (int id, AvisosComercioService servicio, CancellationToken ct) =>
            {
                try
                {
                    var aviso = await servicio.ReenviarAsync(id, ct);
                    return aviso is null ? Results.NotFound() : Results.Ok(aviso);
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
            .WithName("ReenviarAviso");

        return app;
    }
}
