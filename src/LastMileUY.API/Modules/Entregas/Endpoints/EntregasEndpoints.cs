using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Entregas.Services;

namespace LastMileUY.API.Modules.Entregas.Endpoints;

public static class EntregasEndpoints
{
    public static IEndpointRouteBuilder MapEntregasEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/entregas")
            .WithTags("Entregas")
            .RequireAuthorization(p => p.RequireRole("Repartidor"));

        group.MapPost("/{envioId:int}/confirmar", async (
                int envioId,
                ConfirmarEntregaService entregas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                var usuarioId = usuario.FindFirstValue(
                    ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(usuarioId))
                    return Results.Unauthorized();

                try
                {
                    await entregas.ConfirmarAsync(
                        envioId, operadorId, usuarioId, ct);

                    return Results.Ok(new
                    {
                        mensaje = "Entrega confirmada correctamente."
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new { mensaje = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { mensaje = ex.Message });
                }
            })
            .WithName("ConfirmarEntregaRepartidor");

        return app;
    }
}