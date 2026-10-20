using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Entregas.Application;
using LastMileUY.API.Modules.Entregas.Services;
using LastMileUY.Contracts.Entregas;

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
        
        group.MapPost("/{envioId:int}/fallida", async (
                int envioId,
                RegistrarIntentoFallidoRequest request,
                RegistrarIntentoFallidoService entregas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                var usuarioId = usuario.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(usuarioId))
                    return Results.Unauthorized();

                try
                {
                    await entregas.RegistrarAsync(
                        envioId,
                        request.MotivoNoEntregaId,
                        request.Observaciones,
                        operadorId,
                        usuarioId,
                        ct);

                    return Results.Ok(new
                    {
                        mensaje = "Intento fallido registrado correctamente."
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
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { mensaje = ex.Message });
                }
            })
            .WithName("RegistrarEntregaFallida");
        
        group.MapGet("/motivos", async (
                ConsultarMotivosNoEntregaService motivos,
                IContextoOperador contextoOperador,
                CancellationToken ct) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                var resultado = await motivos.ListarActivosAsync(operadorId, ct);

                return Results.Ok(resultado);
            })
            .WithName("ObtenerMotivosNoEntrega");

        return app;
    }
}