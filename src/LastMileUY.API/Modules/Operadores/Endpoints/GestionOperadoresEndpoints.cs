using LastMileUY.Contracts.Administracion;

using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.API.Modules.Operadores.Endpoints;

public static class GestionOperadoresEndpoints
{
    public static IEndpointRouteBuilder MapGestionOperadoresEndpoints(
        this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/admin/operadores")
            .WithTags("Administración de operadores")
            .RequireAuthorization(p => p.RequireRole("Administrador"));

        grupo.MapGet("/", async (
            GestionOperadoresService servicio,
            CancellationToken ct) =>
            Results.Ok(await servicio.ListarAsync(ct)));

        grupo.MapGet("/{id:int}", async (
            int id,
            GestionOperadoresService servicio,
            CancellationToken ct) =>
        {
            var operador = await servicio.ObtenerAsync(id, ct);
            return operador is null
                ? Results.NotFound()
                : Results.Ok(operador);
        });

        grupo.MapPost("/", async (
            CrearOperadorConUsuarioRequest request,
            GestionOperadoresService servicio,
            CancellationToken ct) =>
        {
            try
            {
                var operador = await servicio.CrearConUsuarioAsync(request, ct);
                return Results.Created(
                    $"/api/admin/operadores/{operador.Id}",
                    operador);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        });

        grupo.MapPut("/{id:int}", async (
            int id,
            GuardarOperadorAdminRequest request,
            GestionOperadoresService servicio,
            CancellationToken ct) =>
        {
            try
            {
                var actualizado = await servicio.EditarAsync(id, request, ct);
                return actualizado
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        });

        return app;
    }
}
