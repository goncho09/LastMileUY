using System.Security.Cryptography;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Notificaciones.Application;

// Las suscripciones a avisos del comercio logueado, con el operador que eligió.
// No filtra por comercio a mano: el filtro global de EF y el RLS (ADR-01) ya dejan ver solo las suyas.
// No se borran: el historial de avisos queda atado a la suscripción, así que se desactivan.
public class SuscripcionesAvisosService(
    ApplicationDbContext db,
    IContextoOperador contexto,
    IConfiguration configuracion)
{
    public static readonly string[] TiposPermitidos = [TiposEvento.EnvioAdmitido, TiposEvento.EnvioEstadoCambiado];

    // Igual que el worker: en desarrollo deja usar el receptor de prueba (http y direcciones internas)
    private bool PermitirLocales => configuracion.GetValue<bool>("Avisos:PermitirDestinosLocales");

    public async Task<List<SuscripcionAvisosDto>> ListarAsync(CancellationToken cancellationToken = default) =>
        await db.SuscripcionesAvisos
            .AsNoTracking()
            .OrderByDescending(s => s.FechaCreacion)
            .ThenByDescending(s => s.Id)
            .Select(s => new SuscripcionAvisosDto(s.Id, s.TipoEvento, s.UrlDestino, s.Activa, s.FechaCreacion))
            .ToListAsync(cancellationToken);

    // ArgumentException con el motivo si los datos no sirven
    public async Task<SuscripcionAvisosCreadaDto> CrearAsync(
        CrearSuscripcionAvisosRequest request,
        CancellationToken cancellationToken = default)
    {
        var (operadorId, comercioId) = ComercioActual();

        var tipo = request.TipoEvento?.Trim() ?? string.Empty;
        if (!TiposPermitidos.Contains(tipo))
            throw new ArgumentException("El tipo de evento no existe.");

        var url = request.UrlDestino?.Trim() ?? string.Empty;
        var motivo = DestinosAvisos.Validar(url, PermitirLocales);
        if (motivo is not null)
            throw new ArgumentException(motivo);

        var repetida = await db.SuscripcionesAvisos.AnyAsync(
            s => s.Activa && s.TipoEvento == tipo && s.UrlDestino == url, cancellationToken);
        if (repetida)
            throw new ArgumentException("Ya tenés una suscripción activa a ese evento con esa URL.");

        var suscripcion = new SuscripcionAvisos
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            TipoEvento = tipo,
            UrlDestino = url,
            ClaveFirma = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32)),
            Activa = true
        };

        db.SuscripcionesAvisos.Add(suscripcion);
        await db.SaveChangesAsync(cancellationToken);

        return new SuscripcionAvisosCreadaDto(
            suscripcion.Id,
            suscripcion.TipoEvento,
            suscripcion.UrlDestino,
            suscripcion.Activa,
            suscripcion.FechaCreacion,
            suscripcion.ClaveFirma);
    }

    // null si no existe o es de otro comercio (para el que pregunta, es lo mismo)
    public async Task<SuscripcionAvisosDto?> CambiarActivaAsync(
        int id,
        bool activa,
        CancellationToken cancellationToken = default)
    {
        ComercioActual();

        var suscripcion = await db.SuscripcionesAvisos.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (suscripcion is null)
            return null;

        if (activa && !suscripcion.Activa)
        {
            var repetida = await db.SuscripcionesAvisos.AnyAsync(
                s => s.Id != id && s.Activa && s.TipoEvento == suscripcion.TipoEvento && s.UrlDestino == suscripcion.UrlDestino,
                cancellationToken);
            if (repetida)
                throw new ArgumentException("Ya tenés una suscripción activa a ese evento con esa URL.");
        }

        suscripcion.Activa = activa;
        await db.SaveChangesAsync(cancellationToken);

        return new SuscripcionAvisosDto(
            suscripcion.Id, suscripcion.TipoEvento, suscripcion.UrlDestino, suscripcion.Activa, suscripcion.FechaCreacion);
    }
    
    
    public async Task<SuscripcionAvisosCreadaDto?> RegenerarClaveAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        // Exigir un comercio autenticado con operador seleccionado.
        ComercioActual();

        // El filtro global de EF Core limita las suscripciones
        // al comercio y operador actuales.
        var suscripcion = await db.SuscripcionesAvisos
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (suscripcion is null)
            return null;

        // Reemplazar la clave anterior por una nueva.
        suscripcion.ClaveFirma = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

        await db.SaveChangesAsync(cancellationToken);

        // La clave se devuelve únicamente en esta operación.
        return new SuscripcionAvisosCreadaDto(
            suscripcion.Id,
            suscripcion.TipoEvento,
            suscripcion.UrlDestino,
            suscripcion.Activa,
            suscripcion.FechaCreacion,
            suscripcion.ClaveFirma);
    }


    // Solo un usuario de comercio con un operador elegido
    private (int OperadorId, int ComercioId) ComercioActual() =>
        contexto is { OperadorId: { } operadorId, ComercioId: { } comercioId }
            ? (operadorId, comercioId)
            : throw new UnauthorizedAccessException("Solo un comercio puede configurar sus avisos.");
}
