
using LastMileUY.API.Modules.Comercios.Application;
using LastMileUY.Contracts.Comercios;

namespace LastMileUY.API.Modules.Comercios.Endpoints;

public static class GestionComerciosEndpoints
{
    public static IEndpointRouteBuilder MapGestionComerciosEndpoints(
        this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/admin/comercios")
            .WithTags("Administración de comercios")
            .RequireAuthorization(p => p.RequireRole("Administrador"));

        grupo.MapGet("/", async (
            GestionComerciosService servicio, CancellationToken ct) =>
            Results.Ok(await servicio.ListarAsync(ct)));

        grupo.MapGet("/{id:int}", async (
            int id, GestionComerciosService servicio, CancellationToken ct) =>
        {
            var comercio = await servicio.ObtenerAsync(id, ct);
            return comercio is null
                ? Results.NotFound()
                : Results.Ok(comercio);
        });

        grupo.MapPost("/", async (
            GuardarComercioAdminRequest request,
            GestionComerciosService servicio, CancellationToken ct) =>
        {
            try
            {
                var comercio = await servicio.CrearAsync(request, ct);
                return Results.Created(
                    $"/api/admin/comercios/{comercio.Id}", comercio);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        });

        grupo.MapPut("/{id:int}", async (
            int id, GuardarComercioAdminRequest request,
            GestionComerciosService servicio, CancellationToken ct) =>
        {
            try
            {
                return await servicio.EditarAsync(id, request, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        });

        grupo.MapGet("/{id:int}/operadores", async (
            int id, GestionComerciosService servicio, CancellationToken ct) =>
        {
            if (await servicio.ObtenerAsync(id, ct) is null)
                return Results.NotFound();

            return Results.Ok(await servicio.ListarVinculosAsync(id, ct));
        });

        grupo.MapPost("/{id:int}/operadores/{operadorId:int}", async (
            int id, int operadorId,
            GestionComerciosService servicio, CancellationToken ct) =>
            await servicio.VincularAsync(id, operadorId, ct)
                ? Results.NoContent()
                : Results.NotFound());

        grupo.MapDelete("/{id:int}/operadores/{operadorId:int}", async (
            int id, int operadorId,
            GestionComerciosService servicio, CancellationToken ct) =>
            await servicio.DesvincularAsync(id, operadorId, ct)
                ? Results.NoContent()
                : Results.NotFound());

        return app;
    }
}
