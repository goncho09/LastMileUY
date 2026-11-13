using System.Security.Claims;
using LastMileUY.API.Modules.Integraciones.Application;
using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.API.Modules.Integraciones.Endpoints;

public static class ClavesApiEndpoints
{
    public static IEndpointRouteBuilder MapClavesApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/claves-api")
            .WithTags("Claves de API")
            .RequireAuthorization(politica => politica.RequireRole("Comercio"));

        group.MapGet("/", async (ClavesApiService servicio, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await servicio.ListarAsync(ct));
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .WithName("ListarClavesApi");

        group.MapPost("/", async (
                CrearClaveApiRequest request,
                ClavesApiService servicio,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            {
                try
                {
                    var creada = await servicio.CrearAsync(
                        request, usuario.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, ct);
                    return Results.Created($"/api/claves-api/{creada.Datos.Id}", creada);
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
            .WithName("CrearClaveApi");

        group.MapPost("/{id:int}/revocar", async (int id, ClavesApiService servicio, CancellationToken ct) =>
            {
                try
                {
                    var revocada = await servicio.RevocarAsync(id, ct);
                    return revocada is null ? Results.NotFound() : Results.Ok(revocada);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .WithName("RevocarClaveApi");

        return app;
    }
}
