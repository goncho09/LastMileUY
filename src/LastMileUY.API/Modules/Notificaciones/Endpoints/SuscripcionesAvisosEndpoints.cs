using LastMileUY.API.Modules.Notificaciones.Application;
using LastMileUY.Contracts.Avisos;

namespace LastMileUY.API.Modules.Notificaciones.Endpoints;

// Suscripciones del comercio a avisos HTTP (los manda el worker, ver docs/mensajeria.md)
public static class SuscripcionesAvisosEndpoints
{
    public static IEndpointRouteBuilder MapSuscripcionesAvisosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/avisos/suscripciones")
            .WithTags("Avisos")
            .RequireAuthorization(politica => politica.RequireRole("Comercio"));

        group.MapGet("/", async (SuscripcionesAvisosService servicio, CancellationToken ct) =>
                Results.Ok(await servicio.ListarAsync(ct)))
            .WithName("ListarSuscripcionesAvisos");

        group.MapPost("/", async (
                CrearSuscripcionAvisosRequest request,
                SuscripcionesAvisosService servicio,
                CancellationToken ct) =>
            {
                try
                {
                    var creada = await servicio.CrearAsync(request, ct);
                    return Results.Created($"/api/avisos/suscripciones/{creada.Id}", creada);
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
            .WithName("CrearSuscripcionAvisos");

        group.MapPatch("/{id:int}", async (
                int id,
                CambiarSuscripcionAvisosRequest request,
                SuscripcionesAvisosService servicio,
                CancellationToken ct) =>
            {
                try
                {
                    var suscripcion = await servicio.CambiarActivaAsync(id, request.Activa, ct);
                    return suscripcion is null ? Results.NotFound() : Results.Ok(suscripcion);
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
            .WithName("CambiarSuscripcionAvisos");
        
        
        group.MapPost("/{id:int}/regenerar-clave", async (
                int id,
                SuscripcionesAvisosService servicio,
                CancellationToken ct) =>
            {
                try
                {
                    var suscripcion = await servicio.RegenerarClaveAsync(id, ct);

                    return suscripcion is null
                        ? Results.NotFound()
                        : Results.Ok(suscripcion);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .WithName("RegenerarClaveSuscripcionAvisos");


        return app;
    }
}
