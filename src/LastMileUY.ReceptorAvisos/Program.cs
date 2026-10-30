
using System.Collections.Concurrent;
using System.Globalization;
using LastMileUY.Contracts.Avisos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RegistroClavesAvisos>();
builder.Services.AddSingleton<ClaveAdministrativaReceptor>();

var app = builder.Build();

const int Maximo = 200;
var recibidos = new ConcurrentQueue<AvisoRecibido>();

app.MapPost("/avisos/{**ruta}", async (
    HttpRequest pedido,
    string? ruta,
    int? estado,
    int? demora,
    RegistroClavesAvisos registro,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    // Identificar el aviso.
    if (!Guid.TryParse(
            pedido.Headers[EncabezadosAvisos.AvisoId].ToString(),
            out var avisoId))
    {
        return Results.Unauthorized();
    }

    // Identificar la suscripción que recibe el aviso.
    if (!int.TryParse(
            pedido.Headers[FirmaAvisos.EncabezadoSuscripcionId].ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var suscripcionId) || suscripcionId <= 0)
    {
        return Results.Unauthorized();
    }

    // Obtener la clave correspondiente a esa suscripción.
    // El receptor no consulta la base de datos de LastMileUY.
    var claveFirma = registro.Obtener(suscripcionId);

    if (string.IsNullOrWhiteSpace(claveFirma))
    {
        logger.LogWarning(
            "No existe clave configurada para la suscripción {SuscripcionId}",
            suscripcionId);

        return Results.Unauthorized();
    }

    // Validar el timestamp.
    if (!long.TryParse(
            pedido.Headers[FirmaAvisos.EncabezadoTimestamp].ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var timestamp))
    {
        return Results.Unauthorized();
    }

    // Aceptamos una diferencia máxima de cinco minutos.
    var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    if (timestamp < ahora - 300 || timestamp > ahora + 300)
    {
        logger.LogWarning(
            "Aviso {AvisoId} rechazado por timestamp inválido",
            avisoId);

        return Results.Unauthorized();
    }

    // Obtener la firma recibida.
    var firmaRecibida = pedido.Headers[
        FirmaAvisos.EncabezadoFirma].ToString();

    if (string.IsNullOrWhiteSpace(firmaRecibida))
    {
        return Results.Unauthorized();
    }

    // Leer el contenido exacto que envió el Worker.
    using var lector = new StreamReader(pedido.Body);

    var contenido = await lector.ReadToEndAsync(
        cancellationToken);

    // Verificar la firma HMAC-SHA256.
    if (!FirmaAvisos.Verificar(
            claveFirma,
            suscripcionId,
            avisoId,
            timestamp,
            contenido,
            firmaRecibida))
    {
        logger.LogWarning(
            "Aviso {AvisoId} de suscripción {SuscripcionId} rechazado por firma inválida",
            avisoId,
            suscripcionId);

        return Results.Unauthorized();
    }

    // El aviso ya está autenticado.
    // Conservamos las opciones del receptor de prueba.
    var respuesta = estado ?? StatusCodes.Status200OK;

    var encabezados = pedido.Headers
        .Where(e => e.Key.StartsWith(
            "X-", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(
            e => e.Key,
            e => e.Value.ToString());

    recibidos.Enqueue(new AvisoRecibido(
        DateTime.UtcNow,
        $"/avisos/{ruta}",
        encabezados,
        contenido,
        respuesta));

    while (recibidos.Count > Maximo)
    {
        recibidos.TryDequeue(out _);
    }

    logger.LogInformation(
        "Aviso {AvisoId} de suscripción {SuscripcionId}, " +
        "tipo {Tipo}, intento {Intento}, responde {Estado}",
        avisoId,
        suscripcionId,
        pedido.Headers[EncabezadosAvisos.Tipo].ToString(),
        pedido.Headers[EncabezadosAvisos.Intento].ToString(),
        respuesta);

    if (demora > 0)
    {
        await Task.Delay(
            TimeSpan.FromSeconds(demora.Value),
            cancellationToken);
    }

    return Results.StatusCode(respuesta);
});

app.MapGet("/avisos", () => recibidos.Reverse());

app.MapDelete("/avisos", () =>
{
    recibidos.Clear();
    return Results.NoContent();
});



if (app.Environment.IsDevelopment())
{
    app.MapPost("/configuracion/suscripciones", (
        HttpRequest pedido,
        RegistrarClaveSolicitud solicitud,
        RegistroClavesAvisos registro,
        ClaveAdministrativaReceptor administrador) =>
    {
        var claveRecibida = pedido.Headers[
            "X-Receptor-Admin-Key"].ToString();

        if (!administrador.Verificar(claveRecibida))
            return Results.Unauthorized();

        try
        {
            registro.Registrar(
                solicitud.SuscripcionId,
                solicitud.ClaveFirma);

            return Results.Ok(new
            {
                suscripcionId = solicitud.SuscripcionId,
                mensaje = "Clave registrada correctamente"
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new
            {
                error = ex.Message
            });
        }
    });

    app.MapGet("/configuracion/suscripciones", (
        HttpRequest pedido,
        RegistroClavesAvisos registro,
        ClaveAdministrativaReceptor administrador) =>
    {
        var claveRecibida = pedido.Headers[
            "X-Receptor-Admin-Key"].ToString();

        if (!administrador.Verificar(claveRecibida))
            return Results.Unauthorized();

        return Results.Ok(registro.ListarSuscripciones());
    });
}



app.Run();

public record AvisoRecibido(
    DateTime RecibidoEn,
    string Ruta,
    Dictionary<string, string> Encabezados,
    string Contenido,
    int Respondio);

public record RegistrarClaveSolicitud(
    int SuscripcionId,
    string ClaveFirma);
