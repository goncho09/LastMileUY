using System.Security.Claims;
using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Endpoints;

public static class RutasEndpoints
{
    // Arman, consultan y despachan rutas
    private static readonly string[] PerfilesDePlanificacion = ["Operador", "Despachador"];

    public static IEndpointRouteBuilder MapRutasEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rutas")
            .WithTags("Rutas")
            .RequireAuthorization();

        // Listado paginado: ?pagina=1&tamanioPagina=20&fecha=2026-10-20&estado=Planificada
        group.MapGet("/", async (
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken,
                int pagina = 1,
                int tamanioPagina = 20,
                DateOnly? fecha = null,
                string? estado = null) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                try
                {
                    return Results.Ok(await consultas.ListarAsync(
                        pagina, tamanioPagina, fecha, estado, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDePlanificacion))
            .WithName("ListarRutas");

        // Repartidores, vehículos y envíos disponibles para armar una ruta
        group.MapGet("/opciones", async (
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                return Results.Ok(await consultas.ObtenerOpcionesAsync(cancellationToken));
            })
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDePlanificacion))
            .WithName("OpcionesDeRuta");

        group.MapGet("/{id:int}", async (
                int id,
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                var ruta = await consultas.ObtenerDetalleAsync(id, cancellationToken);

                return ruta is null
                    ? Results.NotFound(new { mensaje = "La ruta no existe." })
                    : Results.Ok(ruta);
            })
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDePlanificacion))
            .WithName("ObtenerRutaPorId");

        group.MapPost("/", async (
                CrearRutaRequest request,
                PlanificarRutaService planificar,
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                try
                {
                    var id = await planificar.CrearAsync(
                        request,
                        usuario.FindFirstValue(ClaimTypes.NameIdentifier),
                        cancellationToken);

                    return Results.Created(
                        $"/api/rutas/{id}",
                        await consultas.ObtenerDetalleAsync(id, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
                catch (AsignacionDuplicadaException ex)
                {
                    return Results.Conflict(new { mensaje = ex.Message });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict(new
                    {
                        mensaje = "Otro usuario modificó alguno de los envíos al mismo tiempo. Volvé a cargarlos."
                    });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDePlanificacion))
            .WithName("CrearRuta");

        group.MapPost("/{id:int}/despachar", async (
                int id,
                PlanificarRutaService planificar,
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                try
                {
                    var existe = await planificar.DespacharAsync(
                        id,
                        usuario.FindFirstValue(ClaimTypes.NameIdentifier),
                        cancellationToken);

                    if (!existe)
                        return Results.NotFound(new { mensaje = "La ruta no existe." });
                }
                catch (EstadoDeRutaException ex)
                {
                    return Results.Conflict(new { mensaje = ex.Message });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict(new
                    {
                        mensaje = "Otro usuario modificó la ruta o sus envíos al mismo tiempo. Volvé a cargarla."
                    });
                }

                return Results.Ok(await consultas.ObtenerDetalleAsync(id, cancellationToken));
            })
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDePlanificacion))
            .WithName("DespacharRuta");

        // App del repartidor: sus rutas despachadas o en curso
        group.MapGet("/mias", async (
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                return Results.Ok(await consultas.ListarDelRepartidorAsync(
                    usuario.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken));
            })
            .RequireAuthorization(politica => politica.RequireRole("Repartidor"))
            .WithName("MisRutas");
        
        // App del repartidor: detalle de una ruta asignada
        group.MapGet("/mias/{id:int}", async (
                int id,
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                var usuarioId = usuario.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(usuarioId))
                    return Results.Unauthorized();

                var ruta = await consultas.ObtenerDetalleDelRepartidorAsync(
                    id, operadorId, usuarioId, cancellationToken);

                return ruta is null
                    ? Results.NotFound(new { mensaje = "Ruta no encontrada." })
                    : Results.Ok(ruta);
            })
            .RequireAuthorization(p => p.RequireRole("Repartidor"))
            .WithName("ObtenerMiRutaPorId");
        
        // App del repartidor: descarga la hoja de ruta (solo si es suya y ya se despachó)
        group.MapGet("/{id:int}/hoja", async (
                int id,
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                CacheDistribuido cache,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                // Los controles se hacen siempre contra la base; solo la hoja armada sale del caché
                var resultado = await consultas.VerificarHojaDelRepartidorAsync(
                    id, usuario.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);

                var hoja = resultado == ResultadoHoja.Disponible
                    ? await cache.ObtenerOCrearAsync(
                        CasosDeCache.HojaDeRuta(operadorId, id),
                        ct => consultas.ObtenerHojaParaLaAppAsync(id, ct),
                        CasosDeCache.DuracionHojaDeRuta,
                        cancellationToken)
                    : null;

                return resultado switch
                {
                    ResultadoHoja.Disponible when hoja is null => Results.NotFound(new { mensaje = "La ruta no existe." }),
                    ResultadoHoja.Disponible => Results.Ok(hoja),
                    ResultadoHoja.DeOtroRepartidor => Results.Forbid(),
                    ResultadoHoja.NoDespachada => Results.Conflict(new
                    {
                        mensaje = "La ruta todavía no se despachó."
                    }),
                    _ => Results.NotFound(new { mensaje = "La ruta no existe." })
                };
            })
            .RequireAuthorization(politica => politica.RequireRole("Repartidor"))
            .WithName("DescargarHojaDeRuta");

        return app;
    }
}
