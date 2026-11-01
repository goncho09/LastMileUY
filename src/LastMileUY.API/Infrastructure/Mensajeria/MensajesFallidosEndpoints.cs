using System.Security.Claims;
using LastMileUY.API.Modules.Mensajeria.Application;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Consulta la cola de fallidos sin consumir ni mover mensajes.
public static class MensajesFallidosEndpoints
{
    
    private static string? LeerEncabezado(
        IDictionary<string, object?>? encabezados,
        string nombre)
    {
        if (encabezados is null ||
            !encabezados.TryGetValue(nombre, out var valor))
            return null;

        return valor switch
        {
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes =>
                System.Text.Encoding.UTF8.GetString(bytes.Span),
            null => null,
            _ => valor.ToString()
        };
    }

    
    public static IEndpointRouteBuilder MapMensajesFallidosEndpoints(
        this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/mensajeria/fallidos")
            .WithTags("Mensajería")
            .RequireAuthorization(politica =>
                politica.RequireRole("Administrador"));

        grupo.MapGet("/resumen", async (
                ConexionRabbitMq rabbit,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                try
                {
                    await using var canal = await rabbit.CrearCanalAsync(ct);

                    var cola = $"{TopologiaMensajeria.ColaWorker}.fallidos";

                    var resultado = await canal.QueueDeclarePassiveAsync(
                        cola, ct);

                    return Results.Ok(new
                    {
                        cantidadPendientes = resultado.MessageCount
                    });
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    loggerFactory.CreateLogger("MensajesFallidos")
                        .LogError(ex,
                            "No se pudo consultar la cola de mensajes fallidos");

                    return Results.Problem(
                        title: "No se pudo consultar RabbitMQ.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName("ResumenMensajesFallidos");
        
        
        grupo.MapPost("/{id:guid}/reintentar", async (
            Guid id,
            ClaimsPrincipal usuario,
            ReintentosMensajesFallidosService servicio,
            CancellationToken ct) =>
        {
            var administradorId =
                usuario.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(administradorId))
                return Results.Unauthorized();

            var solicitado = await servicio.SolicitarAsync(
                id,
                administradorId,
                ct);

            if (!solicitado)
            {
                return Results.Conflict(new
                {
                    mensaje = "El mensaje no existe o no está pendiente."
                });
            }

            return Results.Accepted(
                value: new
                {
                    mensaje = "Reprocesamiento solicitado correctamente."
                });
        });
        
grupo.MapPost("/inspeccionar", async (
    int? limite,
    ConexionRabbitMq rabbit,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var cantidad = limite ?? 10;

    if (cantidad is < 1 or > 20)
        return Results.BadRequest(
            "El límite debe estar entre 1 y 20.");

    try
    {
        await using var canal = await rabbit.CrearCanalAsync(ct);

        var cola = $"{TopologiaMensajeria.ColaWorker}.fallidos";
        var mensajes = new List<object>();
        var entregas = new List<ulong>();

        try
        {
            for (var i = 0; i < cantidad; i++)
            {
                var mensaje = await canal.BasicGetAsync(
                    cola,
                    autoAck: false,
                    cancellationToken: ct);

                if (mensaje is null)
                    break;

                entregas.Add(mensaje.DeliveryTag);

                var propiedades = mensaje.BasicProperties;

                var contenido = System.Text.Encoding.UTF8.GetString(
                    mensaje.Body.Span[
                        ..Math.Min(mensaje.Body.Length, 32768)]);

                mensajes.Add(new
                {
                    mensajeId = propiedades.MessageId,
                    correlacionId = propiedades.CorrelationId,
                    tipoEvento = propiedades.Type,
                    intentos = LeerEncabezado(
                        propiedades.Headers, "x-intentos"),
                    ultimoError = LeerEncabezado(
                        propiedades.Headers, "x-ultimo-error"),
                    contenido,
                    contenidoTruncado = mensaje.Body.Length > 32768
                });
            }
        }
        finally
        {
            // Reencolar todos los mensajes inspeccionados.
            // Si el canal se cierra antes, RabbitMQ recupera
            // automáticamente las entregas sin confirmar.
            foreach (var entrega in entregas)
            {
                await canal.BasicNackAsync(
                    entrega,
                    multiple: false,
                    requeue: true,
                    cancellationToken: CancellationToken.None);
            }
        }

        return Results.Ok(mensajes);
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        loggerFactory.CreateLogger("MensajesFallidos")
            .LogError(ex, "No se pudieron inspeccionar los fallidos");

        return Results.Problem(
            title: "No se pudieron consultar los mensajes.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("InspeccionarMensajesFallidos");
        
grupo.MapGet("/", async (
        [AsParameters] FiltroMensajesFallidos filtro,
        ConsultasMensajesFallidosService servicio,
        CancellationToken ct) =>
    {
        try
        {
            var resultado = await servicio.ListarAsync(filtro, ct);

            return Results.Ok(resultado);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new
            {
                error = ex.Message
            });
        }
    })
    .WithName("ListarMensajesFallidos");

grupo.MapGet("/{id:guid}", async (
        Guid id,
        ConsultasMensajesFallidosService servicio,
        CancellationToken ct) =>
    {
        var resultado = await servicio.ObtenerAsync(id, ct);

        return resultado is null
            ? Results.NotFound()
            : Results.Ok(resultado);
    })
    .WithName("ObtenerMensajeFallido");

        return app;
    }
}