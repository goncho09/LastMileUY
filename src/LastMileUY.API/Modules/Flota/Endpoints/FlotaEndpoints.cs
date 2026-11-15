using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Deposito.Endpoints;
using LastMileUY.API.Modules.Flota.Application;
using LastMileUY.Contracts.Flota;

namespace LastMileUY.API.Modules.Flota.Endpoints;

public static class FlotaEndpoints
{
    public static IEndpointRouteBuilder MapFlotaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/flota", async (FlotaService flota, CancellationToken ct) =>
                Results.Ok(await flota.ObtenerAsync(ct)))
            .RequireAuthorization(p => p.RequireRole("Operador", "Despachador"))
            .WithTags("Flota")
            .WithName("TableroDeFlota");

        app.MapPost("/api/rutas/mias/{id:int}/posiciones", async (
                int id,
                RegistrarPosicionesRequest request,
                FlotaService flota,
                IContextoOperador contexto,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            {
                if (contexto.OperadorId is null)
                    return Results.Forbid();

                var usuarioId = usuario.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(usuarioId))
                    return Results.Unauthorized();

                return await DepositoEndpoints.Ejecutar(async () =>
                    await flota.RegistrarAsync(id, usuarioId, request, ct) is { } resultado
                        ? Results.Ok(resultado)
                        : Results.NotFound(new { mensaje = "Ruta no encontrada." }));
            })
            .RequireAuthorization(p => p.RequireRole("Repartidor"))
            .WithTags("Flota")
            .WithName("RegistrarPosiciones");

        return app;
    }
}
