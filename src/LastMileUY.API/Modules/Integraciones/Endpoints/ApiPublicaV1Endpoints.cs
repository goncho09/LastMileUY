using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Destinatarios.Application;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Integraciones.Application;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Destinatarios;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;
using DestinatarioDto = LastMileUY.Contracts.Destinatarios.DestinatarioDto;
using DireccionDto = LastMileUY.Contracts.Destinatarios.DireccionDto;

namespace LastMileUY.API.Modules.Integraciones.Endpoints;

// API pública para el sistema del comercio (docs/api-publica.md). Versionada en la ruta: lo que está
// en /api/v1 no cambia de forma incompatible; un cambio así iría en /api/v2 y la v1 seguiría andando.
// Solo con clave de API (X-Api-Key), nunca con la cookie del portal. Por dentro usa los mismos
// servicios que el portal, así que las reglas (validaciones, RLS, filtros, eventos) son las mismas.
public static class ApiPublicaV1Endpoints
{
    public const string Version = "v1";

    // Nombre de la política de autorización: clave de API de un comercio
    public const string Politica = "ApiPublica";

    // Topes de pedidos por clave (Program.cs): uno general y uno más bajo para los lotes, que pesan más
    public const string LimiteConsultas = "ApiPublicaConsultas";
    public const string LimiteLotes = "ApiPublicaLotes";

    public static IEndpointRouteBuilder MapApiPublicaV1Endpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1")
            .WithGroupName(Version)
            .RequireAuthorization(Politica)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        var envios = v1.MapGroup("/envios").WithTags("Envíos");

        envios.MapGet("/", async (
                ConsultasEnviosService consultas,
                CancellationToken ct,
                int pagina = 1,
                int tamanioPagina = 20,
                string? estado = null,
                string? buscar = null) =>
            {
                try
                {
                    return Results.Ok(await consultas.ListarAsync(pagina, tamanioPagina, estado, buscar, null, ct));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireRateLimiting(LimiteConsultas)
            .Produces<PaginaDto<EnvioResumenDto>>()
            .WithName("V1ListarEnvios")
            .WithSummary("Lista los envíos del comercio, del más nuevo al más viejo")
            .WithDescription("Filtros opcionales: estado (por ejemplo EnTransito) y buscar (código, referencia o destinatario). Tamaño de página máximo: 100.");

        envios.MapGet("/{id:int}", async (int id, ConsultasEnviosService consultas, CancellationToken ct) =>
                await consultas.ObtenerDetalleAsync(id, ct) is { } envio ? Results.Ok(envio) : Results.NotFound())
            .RequireRateLimiting(LimiteConsultas)
            .Produces<EnvioDetalleDto>()
            .Produces(StatusCodes.Status404NotFound)
            .WithName("V1ObtenerEnvio")
            .WithSummary("Detalle de un envío: estado, bultos, historial e intentos de entrega");

        envios.MapPost("/", async (
                EnvioApiRequest request,
                CrearEnvioService crear,
                IContextoOperador contexto,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            {
                try
                {
                    var envio = await crear.EjecutarAsync(new CrearEnvioRequest
                    {
                        ComercioId = contexto.ComercioId ?? 0,
                        DestinatarioId = request.DestinatarioId,
                        DireccionId = request.DireccionId,
                        ReferenciaExterna = request.ReferenciaExterna,
                        Bultos = request.Bultos ?? []
                    }, usuario, ct);

                    return Results.Created($"/api/v1/envios/{envio.Id}", envio);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
                catch (EnvioDuplicadoException ex)
                {
                    return Results.Conflict(new { mensaje = ex.Message });
                }
            })
            .RequireRateLimiting(LimiteConsultas)
            .Produces<EnvioResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("V1CrearEnvio")
            .WithSummary("Crea un envío")
            .WithDescription("Si se manda una referencia externa que ya existe, responde 409 y no crea otro envío.");

        envios.MapPost("/lote", async (
                ImportarEnviosRequest request,
                ImportarEnviosService importar,
                ClaimsPrincipal usuario,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await importar.ImportarAsync(request, usuario, ct));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensaje = ex.Message });
                }
            })
            .RequireRateLimiting(LimiteLotes)
            .Produces<ImportarEnviosResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithName("V1ImportarEnvios")
            .WithSummary($"Importa hasta {ImportarEnviosService.MaximoPorLote} envíos de una vez, sin duplicar")
            .WithDescription("Cada envío necesita su referencia externa. Reimportar el lote, entero o una parte, no crea envíos repetidos: las referencias que ya existían vuelven como \"Existente\" con el envío que estaba. Cada fila se procesa por separado: una inválida vuelve como \"Rechazado\" con el motivo y no frena a las demás.");

        var destinatarios = v1.MapGroup("/destinatarios").WithTags("Destinatarios");

        destinatarios.MapGet("/", async (ConsultasDestinatariosService consultas, CancellationToken ct) =>
                Results.Ok(await consultas.ListarAsync(null, ct)))
            .RequireRateLimiting(LimiteConsultas)
            .Produces<List<DestinatarioDto>>()
            .WithName("V1ListarDestinatarios")
            .WithSummary("Lista los destinatarios del comercio con sus direcciones activas");

        destinatarios.MapPost("/", async (
                CrearDestinatarioRequest request,
                AltaDestinatariosService alta,
                CancellationToken ct) =>
            {
                try
                {
                    var creado = await alta.CrearAsync(request, ct);
                    return Results.Created($"/api/v1/destinatarios/{creado.Id}", creado);
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
            .RequireRateLimiting(LimiteConsultas)
            .Produces<DestinatarioDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithName("V1CrearDestinatario")
            .WithSummary("Da de alta un destinatario con su primera dirección");

        destinatarios.MapPost("/{id:int}/direcciones", async (
                int id,
                CrearDireccionRequest request,
                AltaDestinatariosService alta,
                CancellationToken ct) =>
            {
                try
                {
                    var creada = await alta.AgregarDireccionAsync(id, request, ct);
                    return creada is null
                        ? Results.NotFound()
                        : Results.Created($"/api/v1/destinatarios/{id}/direcciones/{creada.Id}", creada);
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
            .RequireRateLimiting(LimiteConsultas)
            .Produces<DireccionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("V1AgregarDireccion")
            .WithSummary("Agrega otra dirección de entrega a un destinatario");

        return app;
    }
}
