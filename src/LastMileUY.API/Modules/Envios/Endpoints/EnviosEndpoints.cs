using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Envios.Endpoints;

public static class EnviosEndpoints
{
    public static IEndpointRouteBuilder MapEnviosEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/envios")
            .WithTags("Envíos")
            .RequireAuthorization();
        
        group.MapGet("/", async (
                ApplicationDbContext db,
                CancellationToken cancellationToken) =>
            {
                var envios = await db.Envios
                    .AsNoTracking()
                    .OrderByDescending(e => e.FechaCreacion)
                    .Select(e => new EnvioResponse
                    {
                        Id = e.Id,
                        CodigoSeguimiento = e.CodigoSeguimiento,
                        ComercioId = e.ComercioId,
                        DestinatarioId = e.DestinatarioId,
                        Estado = e.Estado.ToString(),
                        FechaCreacion = e.FechaCreacion
                    })
                    .ToListAsync(cancellationToken);

                return Results.Ok(envios);
            })
            .WithName("ListarEnvios");
        
        group.MapGet("/{id:int}", async (
                int id,
                ApplicationDbContext db,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Unauthorized();

                var envio = await db.Envios
                    .AsNoTracking()
                    .Where(e => e.Id == id)
                    .Select(e => new EnvioResponse
                    {
                        Id = e.Id,
                        CodigoSeguimiento = e.CodigoSeguimiento,
                        ComercioId = e.ComercioId,
                        DestinatarioId = e.DestinatarioId,
                        Estado = e.Estado.ToString(),
                        FechaCreacion = e.FechaCreacion
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (envio is null)
                    return Results.NotFound(new
                    {
                        mensaje = "El envío no existe."
                    });

                return Results.Ok(envio);
            })
            .WithName("ObtenerEnvioPorId");

        group.MapPost("/", async (
                CrearEnvioRequest request,
                CrearEnvioService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var envio = await service.EjecutarAsync(
                        request,
                        cancellationToken);

                    return Results.Created(
                        $"/api/envios/{envio.Id}",
                        envio);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Unauthorized();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new
                    {
                        mensaje = ex.Message
                    });
                }
            })
            .WithName("CrearEnvio");

        return app;
    }
}