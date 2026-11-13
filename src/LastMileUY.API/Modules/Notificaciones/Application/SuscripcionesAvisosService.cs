using System.Security.Cryptography;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.Contracts.Avisos;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Notificaciones.Application;

public class SuscripcionesAvisosService(
    ApplicationDbContext db,
    IContextoOperador contexto,
    IConfiguration configuracion)
{
    private bool PermitirLocales => configuracion.GetValue<bool>("Avisos:PermitirDestinosLocales");

    public async Task<List<SuscripcionAvisosDto>> ListarAsync(CancellationToken cancellationToken = default) =>
        await db.SuscripcionesAvisos
            .AsNoTracking()
            .OrderByDescending(s => s.FechaCreacion)
            .ThenByDescending(s => s.Id)
            .Select(s => new SuscripcionAvisosDto(s.Id, s.TipoEvento, s.UrlDestino, s.Activa, s.FechaCreacion, s.VersionContrato))
            .ToListAsync(cancellationToken);

    public async Task<SuscripcionAvisosCreadaDto> CrearAsync(
        CrearSuscripcionAvisosRequest request,
        CancellationToken cancellationToken = default)
    {
        var (operadorId, comercioId) = ComercioActual();

        if (!VersionesAvisos.Existe(request.Version))
            throw new ArgumentException("La versión de los avisos no existe.");

        var tipo = request.TipoEvento?.Trim() ?? string.Empty;
        if (!VersionesAvisos.Permite(request.Version, tipo))
        {
            throw new ArgumentException(VersionesAvisos.Todas.Any(v => VersionesAvisos.Permite(v, tipo))
                ? $"Ese evento no está en la versión {request.Version} de los avisos: requiere la versión {VersionesAvisos.V2}."
                : "El tipo de evento no existe.");
        }

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
            Activa = true,
            VersionContrato = request.Version
        };

        db.SuscripcionesAvisos.Add(suscripcion);
        await db.SaveChangesAsync(cancellationToken);

        return new SuscripcionAvisosCreadaDto(
            suscripcion.Id,
            suscripcion.TipoEvento,
            suscripcion.UrlDestino,
            suscripcion.Activa,
            suscripcion.FechaCreacion,
            suscripcion.ClaveFirma,
            suscripcion.VersionContrato);
    }

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
            suscripcion.Id, suscripcion.TipoEvento, suscripcion.UrlDestino, suscripcion.Activa, suscripcion.FechaCreacion,
            suscripcion.VersionContrato);
    }


    public async Task<SuscripcionAvisosCreadaDto?> RegenerarClaveAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        ComercioActual();

        var suscripcion = await db.SuscripcionesAvisos
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (suscripcion is null)
            return null;

        suscripcion.ClaveFirma = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

        await db.SaveChangesAsync(cancellationToken);

        return new SuscripcionAvisosCreadaDto(
            suscripcion.Id,
            suscripcion.TipoEvento,
            suscripcion.UrlDestino,
            suscripcion.Activa,
            suscripcion.FechaCreacion,
            suscripcion.ClaveFirma,
            suscripcion.VersionContrato);
    }

    private (int OperadorId, int ComercioId) ComercioActual() =>
        contexto is { OperadorId: { } operadorId, ComercioId: { } comercioId }
            ? (operadorId, comercioId)
            : throw new UnauthorizedAccessException("Solo un comercio puede configurar sus avisos.");
}
