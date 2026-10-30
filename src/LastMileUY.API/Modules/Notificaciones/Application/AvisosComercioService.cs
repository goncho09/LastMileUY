using System.Text.Json;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Comun;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Notificaciones.Application;

// Panel de avisos del comercio (letra 6.9): el historial de entregas y el reenvío de las que fallaron.
// No filtra por comercio a mano: el filtro global de EF y el RLS (ADR-01) ya dejan ver solo los avisos de
// los envíos del comercio logueado, con el operador que eligió.
public class AvisosComercioService(ApplicationDbContext db, IContextoOperador contexto)
{
    public const int TamanioPaginaMaximo = 100;

    public async Task<PaginaDto<AvisoResumenDto>> ListarAsync(
        int pagina,
        int tamanioPagina,
        string? estado,
        CancellationToken cancellationToken = default)
    {
        ComercioActual();

        pagina = Math.Max(1, pagina);
        tamanioPagina = Math.Clamp(tamanioPagina, 1, TamanioPaginaMaximo);

        var consulta = Consulta();

        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (!Enum.TryParse<EstadoEntregaAviso>(estado, ignoreCase: true, out var buscado)
                || !Enum.IsDefined(buscado))
                throw new ArgumentException($"El estado '{estado}' no existe.");

            consulta = consulta.Where(x => x.Aviso.Estado == buscado);
        }

        var total = await consulta.CountAsync(cancellationToken);

        var avisos = await consulta
            .OrderByDescending(x => x.Aviso.FechaCreacion)
            .ThenByDescending(x => x.Aviso.Id)
            .Skip((pagina - 1) * tamanioPagina)
            .Take(tamanioPagina)
            .ToListAsync(cancellationToken);

        return new PaginaDto<AvisoResumenDto>(avisos.Select(Resumen).ToList(), total, pagina, tamanioPagina);
    }

    // null si no existe o es de otro comercio (para el que pregunta, es lo mismo)
    public async Task<AvisoDetalleDto?> ObtenerAsync(int id, CancellationToken cancellationToken = default)
    {
        ComercioActual();

        var fila = await Consulta().SingleOrDefaultAsync(x => x.Aviso.Id == id, cancellationToken);
        if (fila is null)
            return null;

        var intentos = await db.IntentosAvisos
            .AsNoTracking()
            .Where(i => i.EntregaAvisoId == id)
            .OrderBy(i => i.NumeroIntento)
            .Select(i => new IntentoAvisoDto(i.NumeroIntento, i.Fecha, i.Exitoso, i.CodigoHttp, i.Error, i.DuracionMs))
            .ToListAsync(cancellationToken);

        return new AvisoDetalleDto(Resumen(fila), intentos);
    }

    // Vuelve a dejar Pendiente un aviso Fallido: el worker lo manda en unos segundos, como un intento más
    // (queda en el historial). Si ese intento también falla, vuelve a Fallido sin otra ronda de reintentos.
    // null si no existe o es de otro comercio; ArgumentException si no se puede reenviar.
    public async Task<AvisoResumenDto?> ReenviarAsync(int id, CancellationToken cancellationToken = default)
    {
        ComercioActual();

        var aviso = await db.EntregasAvisos.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (aviso is null)
            return null;

        var suscripcion = await db.SuscripcionesAvisos
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == aviso.SuscripcionAvisosId, cancellationToken);
        if (suscripcion is null)
            return null;

        if (aviso.Estado != EstadoEntregaAviso.Fallido)
            throw new ArgumentException(aviso.Estado == EstadoEntregaAviso.Entregado
                ? "El aviso ya fue recibido."
                : "El aviso todavía se está enviando.");

        if (!suscripcion.Activa)
            throw new ArgumentException("La suscripción del aviso está desactivada. Activala para poder reenviarlo.");

        aviso.Estado = EstadoEntregaAviso.Pendiente;
        aviso.FechaProximoIntento = null;
        await db.SaveChangesAsync(cancellationToken);

        return (await ObtenerAsync(id, cancellationToken))?.Aviso;
    }

    // Avisos con el código del envío y la URL de la suscripción. Los tres pasan por el filtro de EF.
    private IQueryable<FilaAviso> Consulta() =>
        from a in db.EntregasAvisos.AsNoTracking()
        join s in db.SuscripcionesAvisos on a.SuscripcionAvisosId equals s.Id
        join e in db.Envios on a.EnvioId equals e.Id
        select new FilaAviso { Aviso = a, CodigoSeguimiento = e.CodigoSeguimiento, UrlDestino = s.UrlDestino };

    private static AvisoResumenDto Resumen(FilaAviso x)
    {
        var (estado, anterior) = EstadosDelEnvio(x.Aviso.Contenido);
        return new(
            x.Aviso.Id,
            x.Aviso.TipoEvento,
            x.CodigoSeguimiento,
            estado,
            anterior,
            x.UrlDestino,
            x.Aviso.Estado.ToString(),
            x.Aviso.CantidadIntentos,
            x.Aviso.FechaCreacion,
            x.Aviso.FechaUltimoIntento,
            x.Aviso.FechaProximoIntento,
            x.Aviso.UltimoError);
    }

    // El contenido es el evento tal cual se le mandó al comercio (EstadoEnvioCambiado en JSON):
    // de ahí sale a qué estado pasó el envío. Si no se puede leer, no se muestra.
    private static (string? Estado, string? Anterior) EstadosDelEnvio(string contenido)
    {
        try
        {
            using var json = JsonDocument.Parse(contenido);
            return (Texto(json.RootElement, "Estado"), Texto(json.RootElement, "EstadoAnterior"));
        }
        catch (JsonException)
        {
            return (null, null);
        }

        static string? Texto(JsonElement raiz, string nombre) =>
            raiz.ValueKind == JsonValueKind.Object
            && raiz.TryGetProperty(nombre, out var valor)
            && valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;
    }

    // Solo un usuario de comercio con un operador elegido
    private void ComercioActual()
    {
        if (contexto is not { OperadorId: not null, ComercioId: not null })
            throw new UnauthorizedAccessException("Solo un comercio puede consultar sus avisos.");
    }

    private class FilaAviso
    {
        public EntregaAviso Aviso { get; init; } = null!;
        public string CodigoSeguimiento { get; init; } = string.Empty;
        public string UrlDestino { get; init; } = string.Empty;
    }
}
