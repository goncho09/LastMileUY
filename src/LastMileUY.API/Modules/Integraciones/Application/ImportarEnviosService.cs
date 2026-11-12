using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Integraciones.Application;

// Importación masiva de envíos (req. 7): reimportar un lote, completo o una parte, no duplica envíos.
// La clave es la referencia externa (el número de pedido en el sistema del comercio): hay un índice
// único por operador + comercio + referencia. Si la referencia ya existe, la fila vuelve como
// "Existente" con el envío que ya estaba, y no se toca.
// Cada fila se guarda por separado: una fila inválida no frena a las demás, y el comercio puede
// corregirla y volver a mandar el lote entero sin miedo.
public class ImportarEnviosService(
    ApplicationDbContext db,
    CrearEnvioService crear,
    IContextoOperador contexto)
{
    public const int MaximoPorLote = 100;

    public async Task<ImportarEnviosResponse> ImportarAsync(
        ImportarEnviosRequest request, ClaimsPrincipal usuario, CancellationToken ct = default)
    {
        var comercioId = contexto.ComercioId
            ?? throw new UnauthorizedAccessException("La importación es para el sistema de un comercio.");

        var filas = request.Envios ?? [];

        if (filas.Count == 0)
            throw new ArgumentException("El lote no tiene envíos.");

        if (filas.Count > MaximoPorLote)
            throw new ArgumentException($"Un lote puede tener como máximo {MaximoPorLote} envíos; partilo en varios.");

        var resultados = new List<ResultadoImportacionDto>(filas.Count);

        for (var indice = 0; indice < filas.Count; indice++)
        {
            resultados.Add(await ImportarFilaAsync(indice, filas[indice], comercioId, usuario, ct));
        }

        return new ImportarEnviosResponse(
            resultados.Count(r => r.Resultado == ResultadosImportacion.Creado),
            resultados.Count(r => r.Resultado == ResultadosImportacion.Existente),
            resultados.Count(r => r.Resultado == ResultadosImportacion.Rechazado),
            resultados);
    }

    private async Task<ResultadoImportacionDto> ImportarFilaAsync(
        int indice, EnvioApiRequest? fila, int comercioId, ClaimsPrincipal usuario, CancellationToken ct)
    {
        var referencia = fila?.ReferenciaExterna?.Trim();

        if (fila is null || string.IsNullOrEmpty(referencia))
            return Rechazada(indice, referencia, "Cada envío del lote necesita su referencia externa.");

        if (await ExistenteAsync(indice, referencia, ct) is { } existente)
            return existente;

        try
        {
            var envio = await crear.EjecutarAsync(new CrearEnvioRequest
            {
                ComercioId = comercioId,
                DestinatarioId = fila.DestinatarioId,
                DireccionId = fila.DireccionId,
                ReferenciaExterna = referencia,
                Bultos = fila.Bultos ?? []
            }, usuario, ct);

            return new ResultadoImportacionDto(
                indice, referencia, ResultadosImportacion.Creado, envio.Id, envio.CodigoSeguimiento, null);
        }
        catch (ArgumentException ex)
        {
            return Rechazada(indice, referencia, ex.Message);
        }
        catch (EnvioDuplicadoException)
        {
            // Otro pedido la creó entre la consulta y el guardado (dos importaciones a la vez):
            // el índice único lo frenó, y para quien importa es lo mismo que si ya existía
            db.ChangeTracker.Clear();
            return await ExistenteAsync(indice, referencia, ct)
                   ?? Rechazada(indice, referencia, "No se pudo guardar el envío; volvé a intentar.");
        }
        finally
        {
            // Un envío que no se pudo guardar queda en el contexto y el próximo guardado lo volvería a
            // intentar: cada fila arranca limpia
            db.ChangeTracker.Clear();
        }
    }

    private async Task<ResultadoImportacionDto?> ExistenteAsync(int indice, string referencia, CancellationToken ct)
    {
        var envio = await db.Envios
            .AsNoTracking()
            .Where(e => e.ReferenciaExterna == referencia)
            .Select(e => new { e.Id, e.CodigoSeguimiento })
            .FirstOrDefaultAsync(ct);

        return envio is null
            ? null
            : new ResultadoImportacionDto(indice, referencia, ResultadosImportacion.Existente,
                envio.Id, envio.CodigoSeguimiento, "Ya existía un envío con esa referencia; no se creó otro.");
    }

    private static ResultadoImportacionDto Rechazada(int indice, string? referencia, string mensaje) =>
        new(indice, referencia, ResultadosImportacion.Rechazado, null, null, mensaje);
}
