using System.Security.Claims;
using LastMileUY.API.Modules.Integraciones.Application;
using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.API.Modules.Integraciones.Endpoints;

// El comercio administra sus claves de la API pública desde el portal, con su sesión (cookie).
// Con una clave no se pueden crear ni revocar claves: estos endpoints no aceptan ese esquema.
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

        // 201 con la clave completa: es la única vez que se ve
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

        // Deja de funcionar en el pedido siguiente. Revocar dos veces no cambia nada
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
