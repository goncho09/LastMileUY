using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.API.Modules.Sincronizacion.Endpoints;

public static class SincronizacionEndpoints
{
    private static readonly string[] PersonalDelOperador = ["Operador", "Despachador"];

    private static readonly string[] RepartidorYPersonal = ["Repartidor", "Operador", "Despachador"];

    public static IEndpointRouteBuilder MapSincronizacionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sincronizacion")
            .WithTags("Sincronización")
            .RequireAuthorization();

        // App del repartidor: manda lo que registró sin conexión. Responde 200 con un resultado
        // por operación (400 solo si el lote viene vacío o es demasiado grande).
        group.MapPost("/", async (
                SincronizarRequest request,
                SincronizarEntregasService sincronizar,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                try
                {
                    return Results.Ok(await sincronizar.EjecutarAsync(
                        request, usuario.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole("Repartidor"))
            .WithName("Sincronizar");

        // Motivos de no entrega del operador (la app los descarga junto con la hoja de ruta)
        group.MapGet("/motivos", async (
                ConsultasSincronizacionService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                return Results.Ok(await consultas.ListarMotivosAsync(cancellationToken));
            })
            .RequireAuthorization(politica => politica.RequireRole(RepartidorYPersonal))
            .WithName("MotivosNoEntrega");

        // Backoffice: lo que llegó de la app, con su resultado. ?rutaId=3&resultado=Conflicto&pagina=1
        group.MapGet("/operaciones", async (
                ConsultasSincronizacionService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken,
                int pagina = 1,
                int tamanioPagina = 20,
                int? rutaId = null,
                string? resultado = null) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                try
                {
                    return Results.Ok(await consultas.ListarOperacionesAsync(
                        pagina, tamanioPagina, rutaId, resultado, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole(PersonalDelOperador))
            .WithName("OperacionesSincronizadas");

        // Backoffice: la imagen de la firma o la foto de una entrega
        group.MapGet("/evidencias/{intentoId:int}/{tipo}", async (
                int intentoId,
                string tipo,
                ConsultasSincronizacionService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                try
                {
                    var imagen = await consultas.ObtenerEvidenciaAsync(intentoId, tipo, cancellationToken);

                    return imagen is null
                        ? Results.NotFound(new { mensaje = "La evidencia no existe." })
                        : Results.File(imagen.Contenido, imagen.TipoContenido);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole(PersonalDelOperador))
            .WithName("EvidenciaDeEntrega");

        return app;
    }
}
