using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.API.Modules.Envios.Endpoints;

public static class EnviosEndpoints
{
    public static IEndpointRouteBuilder MapEnviosEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/envios")
            .WithTags("Envíos")
            .RequireAuthorization();
        
        // Listado paginado: ?pagina=1&tamanioPagina=20&estado=EnTransito&buscar=texto
        group.MapGet("/", async (
                ConsultasEnviosService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken,
                int pagina = 1,
                int tamanioPagina = 20,
                string? estado = null,
                string? buscar = null) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Unauthorized();

                try
                {
                    var envios = await consultas.ListarAsync(
                        pagina, tamanioPagina, estado, buscar, cancellationToken);

                    return Results.Ok(envios);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new
                    {
                        mensaje = ex.Message
                    });
                }
            })
            .WithName("ListarEnvios");

        group.MapGet("/{id:int}", async (
                int id,
                ConsultasEnviosService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Unauthorized();

                var envio = await consultas.ObtenerDetalleAsync(id, cancellationToken);

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