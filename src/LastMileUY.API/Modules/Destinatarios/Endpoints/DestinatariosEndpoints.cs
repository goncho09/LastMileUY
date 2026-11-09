using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Destinatarios.Application;
using LastMileUY.Contracts.Destinatarios;

namespace LastMileUY.API.Modules.Destinatarios.Endpoints;

public static class DestinatariosEndpoints
{
    public static IEndpointRouteBuilder MapDestinatariosEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/destinatarios")
            .WithTags("Destinatarios")
            .RequireAuthorization();

        // Listado con direcciones activas: ?comercioId=1 (opcional)
        group.MapGet("/", async (
                ConsultasDestinatariosService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken,
                int? comercioId = null) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Unauthorized();

                return Results.Ok(await consultas.ListarAsync(comercioId, cancellationToken));
            })
            // Trae nombre, email, teléfono y direcciones: solo para quien da de alta envíos
            // (los mismos perfiles que POST /api/envios). El repartidor ve lo justo en su hoja de ruta.
            .RequireAuthorization(politica => politica.RequireRole("Comercio", "Operador"))
            .WithName("ListarDestinatarios");

        // Alta con la primera dirección. 201 con el destinatario; 400 datos inválidos o comercio que no
        // trabaja con el operador; 403 un usuario de comercio que pide otro comercio
        group.MapPost("/", async (
                CrearDestinatarioRequest request,
                AltaDestinatariosService alta,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var creado = await alta.CrearAsync(request, cancellationToken);
                    return Results.Created($"/api/destinatarios/{creado.Id}", creado);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole("Comercio", "Operador"))
            .WithName("CrearDestinatario");

        // Otra dirección de entrega. 404 si el destinatario no existe o es de otro comercio
        group.MapPost("/{id:int}/direcciones", async (
                int id,
                CrearDireccionRequest request,
                AltaDestinatariosService alta,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var creada = await alta.AgregarDireccionAsync(id, request, cancellationToken);
                    return creada is null
                        ? Results.NotFound()
                        : Results.Created($"/api/destinatarios/{id}/direcciones/{creada.Id}", creada);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole("Comercio", "Operador"))
            .WithName("AgregarDireccion");

        return app;
    }
}
