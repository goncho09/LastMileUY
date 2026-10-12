using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
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
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var envio = await service.EjecutarAsync(
                        request,
                        usuario.FindFirstValue(ClaimTypes.NameIdentifier),
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
                catch (EnvioDuplicadoException ex)
                {
                    return Results.Conflict(new
                    {
                        mensaje = ex.Message
                    });
                }
            })
            .WithName("CrearEnvio");

        // Cambio de estado desde el Backoffice. Solo el personal del operador.
        group.MapPatch("/{id:int}/estado", async (
                int id,
                CambiarEstadoRequest request,
                CambiarEstadoEnvioService cambiarEstado,
                ConsultasEnviosService consultas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Unauthorized();

                try
                {
                    var existe = await cambiarEstado.EjecutarAsync(
                        id,
                        request,
                        origen: "Backoffice",
                        usuario.FindFirstValue(ClaimTypes.NameIdentifier),
                        cancellationToken);

                    if (!existe)
                        return Results.NotFound(new
                        {
                            mensaje = "El envío no existe."
                        });
                }
                catch (Exception ex) when (ex is ArgumentException or TransicionInvalidaException)
                {
                    return Results.BadRequest(new
                    {
                        mensaje = ex.Message
                    });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict(new
                    {
                        mensaje = "Otro usuario modificó el envío al mismo tiempo. Volvé a cargarlo."
                    });
                }

                return Results.Ok(await consultas.ObtenerDetalleAsync(id, cancellationToken));
            })
            .RequireAuthorization(politica => politica.RequireRole("Operador", "Despachador"))
            .WithName("CambiarEstadoEnvio");

        return app;
    }
}