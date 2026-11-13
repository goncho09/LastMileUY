using LastMileUY.API.Modules.Entregas.Application;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Tarifas.Application;
using LastMileUY.Contracts.Configuracion;

namespace LastMileUY.API.Modules.Operadores.Endpoints;

public static class ConfiguracionOperadorEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracionOperadorEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/operador")
            .WithTags("Configuración del operador")
            .RequireAuthorization(politica => politica.RequireRole("Operador"));

        MapZonas(grupo);
        MapVehiculos(grupo);
        MapMotivos(grupo);
        MapReglasOperativas(grupo);
        MapCuadrosTarifarios(grupo);

        return app;
    }

    private static void MapZonas(RouteGroupBuilder grupo)
    {
        var zonas = grupo.MapGroup("/zonas");

        zonas.MapGet("/", async (ZonasService s, CancellationToken ct) => Results.Ok(await s.ListarAsync(ct)))
            .WithName("ListarZonas");

        zonas.MapGet("/{id:int}", async (int id, ZonasService s, CancellationToken ct) =>
                await s.ObtenerAsync(id, ct) is { } zona ? Results.Ok(zona) : Results.NotFound())
            .WithName("ObtenerZona");

        zonas.MapPost("/", (GuardarZonaRequest request, ZonasService s, CancellationToken ct) =>
                Ejecutar(async () =>
                {
                    var zona = await s.CrearAsync(request, ct);
                    return Results.Created($"/api/operador/zonas/{zona.Id}", zona);
                }))
            .WithName("CrearZona");

        zonas.MapPut("/{id:int}", (int id, GuardarZonaRequest request, ZonasService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.EditarAsync(id, request, ct))))
            .WithName("EditarZona");

        zonas.MapPut("/{id:int}/activa", (int id, CambiarActivoRequest request, ZonasService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.CambiarActivaAsync(id, request.Activo, ct))))
            .WithName("CambiarZonaActiva");

        zonas.MapPost("/{zonaId:int}/franjas", (int zonaId, GuardarFranjaHorariaRequest request, ZonasService s, CancellationToken ct) =>
                Ejecutar(async () => await s.CrearFranjaAsync(zonaId, request, ct) is { } franja
                    ? Results.Created($"/api/operador/zonas/{zonaId}", franja)
                    : Results.NotFound()))
            .WithName("CrearFranjaHoraria");

        zonas.MapPut("/{zonaId:int}/franjas/{id:int}", (int zonaId, int id, GuardarFranjaHorariaRequest request, ZonasService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.EditarFranjaAsync(zonaId, id, request, ct))))
            .WithName("EditarFranjaHoraria");

        zonas.MapPut("/{zonaId:int}/franjas/{id:int}/activa", (int zonaId, int id, CambiarActivoRequest request, ZonasService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.CambiarFranjaActivaAsync(zonaId, id, request.Activo, ct))))
            .WithName("CambiarFranjaHorariaActiva");
    }

    private static void MapVehiculos(RouteGroupBuilder grupo)
    {
        var vehiculos = grupo.MapGroup("/vehiculos");

        vehiculos.MapGet("/", async (VehiculosService s, CancellationToken ct) => Results.Ok(await s.ListarAsync(ct)))
            .WithName("ListarVehiculos");

        vehiculos.MapPost("/", (GuardarVehiculoRequest request, VehiculosService s, CancellationToken ct) =>
                Ejecutar(async () =>
                {
                    var vehiculo = await s.CrearAsync(request, ct);
                    return Results.Created("/api/operador/vehiculos", vehiculo);
                }))
            .WithName("CrearVehiculo");

        vehiculos.MapPut("/{id:int}", (int id, GuardarVehiculoRequest request, VehiculosService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.EditarAsync(id, request, ct))))
            .WithName("EditarVehiculo");

        vehiculos.MapPut("/{id:int}/activo", (int id, CambiarActivoRequest request, VehiculosService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.CambiarActivoAsync(id, request.Activo, ct))))
            .WithName("CambiarVehiculoActivo");
    }

    private static void MapMotivos(RouteGroupBuilder grupo)
    {
        var motivos = grupo.MapGroup("/motivos-no-entrega");

        motivos.MapGet("/", async (MotivosNoEntregaService s, CancellationToken ct) => Results.Ok(await s.ListarAsync(ct)))
            .WithName("ListarMotivosNoEntregaConfiguracion");

        motivos.MapPost("/", (GuardarMotivoNoEntregaRequest request, MotivosNoEntregaService s, CancellationToken ct) =>
                Ejecutar(async () =>
                {
                    var motivo = await s.CrearAsync(request, ct);
                    return Results.Created("/api/operador/motivos-no-entrega", motivo);
                }))
            .WithName("CrearMotivoNoEntrega");

        motivos.MapPut("/{id:int}", (int id, GuardarMotivoNoEntregaRequest request, MotivosNoEntregaService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.EditarAsync(id, request, ct))))
            .WithName("EditarMotivoNoEntrega");

        motivos.MapPut("/{id:int}/activo", (int id, CambiarActivoRequest request, MotivosNoEntregaService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.CambiarActivoAsync(id, request.Activo, ct))))
            .WithName("CambiarMotivoNoEntregaActivo");
    }

    private static void MapReglasOperativas(RouteGroupBuilder grupo)
    {
        var reglas = grupo.MapGroup("/reglas-operativas");

        reglas.MapGet("/", async (ReglasOperativasService s, CancellationToken ct) => Results.Ok(await s.ListarAsync(ct)))
            .WithName("ListarReglasOperativas");

        reglas.MapPost("/", (GuardarReglaOperativaRequest request, ReglasOperativasService s, CancellationToken ct) =>
                Ejecutar(async () =>
                {
                    var regla = await s.CrearAsync(request, ct);
                    return Results.Created("/api/operador/reglas-operativas", regla);
                }))
            .WithName("CrearReglaOperativa");

        reglas.MapPut("/{id:int}", (int id, GuardarReglaOperativaRequest request, ReglasOperativasService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.EditarAsync(id, request, ct))))
            .WithName("EditarReglaOperativa");

        reglas.MapPut("/{id:int}/activa", (int id, CambiarActivoRequest request, ReglasOperativasService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.CambiarActivaAsync(id, request.Activo, ct))))
            .WithName("CambiarReglaOperativaActiva");
    }

    private static void MapCuadrosTarifarios(RouteGroupBuilder grupo)
    {
        var cuadros = grupo.MapGroup("/cuadros-tarifarios");

        cuadros.MapGet("/", async (CuadrosTarifariosService s, CancellationToken ct) => Results.Ok(await s.ListarAsync(ct)))
            .WithName("ListarCuadrosTarifarios");

        cuadros.MapGet("/{id:int}", async (int id, CuadrosTarifariosService s, CancellationToken ct) =>
                await s.ObtenerAsync(id, ct) is { } cuadro ? Results.Ok(cuadro) : Results.NotFound())
            .WithName("ObtenerCuadroTarifario");

        cuadros.MapPost("/", (CrearCuadroTarifarioRequest request, CuadrosTarifariosService s, CancellationToken ct) =>
                Ejecutar(async () =>
                {
                    var cuadro = await s.CrearAsync(request, ct);
                    return Results.Created($"/api/operador/cuadros-tarifarios/{cuadro.Id}", cuadro);
                }))
            .WithName("CrearCuadroTarifario");

        cuadros.MapPost("/{cuadroId:int}/tarifas", (int cuadroId, GuardarTarifaRequest request, CuadrosTarifariosService s, CancellationToken ct) =>
                Ejecutar(async () => await s.AgregarTarifaAsync(cuadroId, request, ct) is { } tarifa
                    ? Results.Created($"/api/operador/cuadros-tarifarios/{cuadroId}", tarifa)
                    : Results.NotFound()))
            .WithName("AgregarTarifa");

        cuadros.MapPut("/{cuadroId:int}/tarifas/{id:int}", (int cuadroId, int id, GuardarTarifaRequest request, CuadrosTarifariosService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.EditarTarifaAsync(cuadroId, id, request, ct))))
            .WithName("EditarTarifa");

        cuadros.MapPut("/{cuadroId:int}/tarifas/{id:int}/activa", (int cuadroId, int id, CambiarActivoRequest request, CuadrosTarifariosService s, CancellationToken ct) =>
                Ejecutar(async () => SinContenido(await s.CambiarTarifaActivaAsync(cuadroId, id, request.Activo, ct))))
            .WithName("CambiarTarifaActiva");
    }

    private static IResult SinContenido(bool encontrado) => encontrado ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> Ejecutar(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { mensaje = ex.Message });
        }
        catch (ConflictoConfiguracionException ex)
        {
            return Results.Conflict(new { mensaje = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }
}
