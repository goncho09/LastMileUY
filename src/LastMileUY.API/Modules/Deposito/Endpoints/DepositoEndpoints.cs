using System.Security.Claims;
using LastMileUY.API.Modules.Deposito.Application;
using LastMileUY.Contracts.Deposito;

namespace LastMileUY.API.Modules.Deposito.Endpoints;

public static class DepositoEndpoints
{
    public static readonly string[] PerfilesDeposito = ["Operador", "Despachador", "OperarioDeposito"];

    public static IEndpointRouteBuilder MapDepositoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/deposito")
            .WithTags("Depósito")
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDeposito));

        grupo.MapGet("/por-recibir", async (RecepcionDepositoService servicio, CancellationToken ct) =>
                Results.Ok(await servicio.PendientesAsync(ct)))
            .WithName("EnviosPorRecibir");

        grupo.MapGet("/recepcion/{codigo}", async (string codigo, RecepcionDepositoService servicio, CancellationToken ct) =>
                await servicio.BuscarAsync(codigo, ct) is { } envio ? Results.Ok(envio) : Results.NotFound())
            .WithName("BuscarEnvioParaRecibir");

        grupo.MapPost("/recepciones", async (
                RegistrarRecepcionRequest request,
                RecepcionDepositoService servicio,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            await Ejecutar(async () =>
                await servicio.RegistrarAsync(request, usuario.FindFirstValue(ClaimTypes.NameIdentifier), ct) is { } recepcion
                    ? Results.Created($"/api/deposito/recepciones/{recepcion.Id}", recepcion)
                    : Results.NotFound()))
            .WithName("RegistrarRecepcion");

        grupo.MapGet("/discrepancias", async (bool? pendientes, RecepcionDepositoService servicio, CancellationToken ct) =>
                Results.Ok(await servicio.DiscrepanciasAsync(pendientes ?? true, ct)))
            .WithName("ListarDiscrepancias");

        grupo.MapPost("/discrepancias/{id:int}/resolver", async (
                int id,
                ResolverDiscrepanciaRequest request,
                RecepcionDepositoService servicio,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            await Ejecutar(async () =>
                await servicio.ResolverAsync(id, request.Resolucion, usuario.FindFirstValue(ClaimTypes.NameIdentifier), ct)
                    ? Results.NoContent()
                    : Results.NotFound()))
            .WithName("ResolverDiscrepancia");

        return app;
    }

    internal static async Task<IResult> Ejecutar(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { mensaje = ex.Message });
        }
        catch (ConflictoDepositoException ex)
        {
            return Results.Conflict(new { mensaje = ex.Message });
        }
    }
}
