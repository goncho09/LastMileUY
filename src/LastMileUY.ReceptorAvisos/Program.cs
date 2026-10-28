using System.Collections.Concurrent;
using LastMileUY.Contracts.Avisos;

// Receptor de avisos de prueba: hace de comercio, para probar los avisos sin un servidor real (ver
// docs/mensajeria.md). Solo para desarrollo y pruebas.
// - POST /avisos/...: recibe un aviso, lo guarda en memoria (los últimos 200) y responde 200.
//   Para probar fallos: ?estado=500 responde ese código y ?demora=15 tarda esos segundos.
// - GET /avisos: los recibidos, el último primero. DELETE /avisos: los borra.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const int Maximo = 200;
var recibidos = new ConcurrentQueue<AvisoRecibido>();

// La ruta después de /avisos es libre: sirve para distinguir suscripciones (/avisos/comercio-1)
app.MapPost("/avisos/{**ruta}", async (
    HttpRequest pedido, string? ruta, int? estado, int? demora, ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    using var lector = new StreamReader(pedido.Body);
    var contenido = await lector.ReadToEndAsync(cancellationToken);
    var respuesta = estado ?? StatusCodes.Status200OK;

    // Los encabezados propios del aviso (X-Aviso-*, X-Correlation-Id, la firma cuando la haya)
    var encabezados = pedido.Headers
        .Where(e => e.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(e => e.Key, e => e.Value.ToString());

    recibidos.Enqueue(new AvisoRecibido(DateTime.UtcNow, $"/avisos/{ruta}", encabezados, contenido, respuesta));
    while (recibidos.Count > Maximo)
        recibidos.TryDequeue(out _);

    logger.LogInformation(
        "Aviso {AvisoId} ({Tipo}, intento {Intento}) en /avisos/{Ruta}: responde {Estado}",
        pedido.Headers[EncabezadosAvisos.AvisoId].ToString(), pedido.Headers[EncabezadosAvisos.Tipo].ToString(),
        pedido.Headers[EncabezadosAvisos.Intento].ToString(), ruta, respuesta);

    if (demora > 0)
        await Task.Delay(TimeSpan.FromSeconds(demora.Value), cancellationToken);

    return Results.StatusCode(respuesta);
});

app.MapGet("/avisos", () => recibidos.Reverse());

app.MapDelete("/avisos", () =>
{
    recibidos.Clear();
    return Results.NoContent();
});

app.Run();

// Respondio: el código con el que contestó el receptor. Público: las pruebas lo usan para levantar el receptor
public record AvisoRecibido(
    DateTime RecibidoEn, string Ruta, Dictionary<string, string> Encabezados, string Contenido, int Respondio);
