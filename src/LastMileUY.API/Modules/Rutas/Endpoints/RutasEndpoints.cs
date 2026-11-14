using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Deposito.Application;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Deposito;
using LastMileUY.Contracts.Operadores;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Endpoints;

public static class RutasEndpoints
{
    private static readonly string[] PerfilesDePlanificacion = ["Operador", "Despachador"];

    public static IEndpointRouteBuilder MapRutasEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rutas")
            .WithTags("Rutas")
            .RequireAuthorization();

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

        group.MapPost("/sugerir-orden", async (
                SugerirOrdenRequest request,
                SugerirOrdenService servicio,
                FuncionalidadesService funcionalidades,
                IContextoOperador contextoOperador,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                if (!await funcionalidades.EstaActivaAsync(operadorId, Funcionalidades.OrdenSugerido, cancellationToken))
                    return FuncionalidadesService.Desactivada(Funcionalidades.OrdenSugerido);

                try
                {
                    return Results.Ok(await servicio.SugerirAsync(request, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireAuthorization(politica => politica.RequireRole(PerfilesDePlanificacion))
            .WithName("SugerirOrdenDeParadas");

        group.MapPost("/{id:int}/despachar", async (
                int id,
                PlanificarRutaService planificar,
                ConsultasRutasService consultas,
                FuncionalidadesService funcionalidades,
                CargaVehiculoService carga,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is not int operadorId)
                    return Results.Forbid();

                if (await funcionalidades.EstaActivaAsync(operadorId, Funcionalidades.VerificacionCarga, cancellationToken)
                    && await carga.EstadoCargaAsync(id, cancellationToken) != EstadosCarga.Verificada)
                {
                    return Results.Conflict(new
                    {
                        mensaje = "Antes de despachar, el depósito tiene que verificar que todos los bultos están cargados en el vehículo."
                    });
                }

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

        group.MapGet("/{id:int}/hoja", async (
                int id,
                ConsultasRutasService consultas,
                IContextoOperador contextoOperador,
                ClaimsPrincipal usuario,
                CancellationToken cancellationToken) =>
            {
                if (contextoOperador.OperadorId is null)
                    return Results.Forbid();

                var (resultado, hoja) = await consultas.ObtenerHojaDelRepartidorAsync(
                    id, usuario.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);

                return resultado switch
                {
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
