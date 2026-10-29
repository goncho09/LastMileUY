using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Destinatarios.Application;

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

        return app;
    }
}
