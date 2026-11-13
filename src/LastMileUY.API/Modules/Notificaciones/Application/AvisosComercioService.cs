using System.Text.Json;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Comun;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Notificaciones.Application;

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

        tamanioPagina = Math.Clamp(tamanioPagina, 1, TamanioPaginaMaximo);
        pagina = Math.Clamp(pagina, 1, int.MaxValue / tamanioPagina);

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

    public async Task<AvisoResumenDto?> ReenviarAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        ComercioActual();

        var aviso = await db.EntregasAvisos
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (aviso is null)
            return null;

        var suscripcion = await db.SuscripcionesAvisos
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == aviso.SuscripcionAvisosId,
                cancellationToken);

        if (suscripcion is null)
            return null;

        if (aviso.Estado != EstadoEntregaAviso.Fallido)
            throw new ArgumentException(
                aviso.Estado == EstadoEntregaAviso.Entregado
                    ? "El aviso ya fue recibido."
                    : "El aviso todavía se está enviando.");

        if (!suscripcion.Activa)
            throw new ArgumentException(
                "La suscripción del aviso está desactivada. Activala para poder reenviarlo.");

        var actualizados = await db.EntregasAvisos
            .Where(a =>
                a.Id == id &&
                a.Estado == EstadoEntregaAviso.Fallido)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        a => a.Estado,
                        EstadoEntregaAviso.Pendiente)
                    .SetProperty(
                        a => a.FechaProximoIntento,
                        (DateTime?)null),
                cancellationToken);

        if (actualizados == 0)
            throw new ArgumentException(
                "El aviso ya fue reenviado o cambió de estado.");

        return (await ObtenerAsync(id, cancellationToken))?.Aviso;
    }

    private IQueryable<FilaAviso> Consulta() =>
        from a in db.EntregasAvisos.AsNoTracking()
        join s in db.SuscripcionesAvisos on a.SuscripcionAvisosId equals s.Id
        join e in db.Envios on a.EnvioId equals e.Id
        select new FilaAviso { Aviso = a, CodigoSeguimiento = e.CodigoSeguimiento, UrlDestino = s.UrlDestino };

    private static AvisoResumenDto Resumen(FilaAviso x)
    {
        var leido = LeerContenido(x.Aviso.Contenido);
        return new(
            x.Aviso.Id,
            x.Aviso.TipoEvento,
            x.CodigoSeguimiento,
            leido.Estado,
            leido.Anterior,
            x.UrlDestino,
            x.Aviso.Estado.ToString(),
            x.Aviso.CantidadIntentos,
            x.Aviso.FechaCreacion,
            x.Aviso.FechaUltimoIntento,
            x.Aviso.FechaProximoIntento,
            x.Aviso.UltimoError,
            leido.CodigoBulto,
            leido.Motivo,
            leido.Version);
    }

    private record Contenido(string? Estado, string? Anterior, string? CodigoBulto, string? Motivo, int Version);

    private static Contenido LeerContenido(string contenido)
    {
        try
        {
            using var json = JsonDocument.Parse(contenido);
            var raiz = json.RootElement;

            if (raiz.ValueKind == JsonValueKind.Object
                && raiz.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.GetInt32() == VersionesAvisos.V2)
            {
                if (raiz.TryGetProperty("bulto", out var bulto) && bulto.ValueKind == JsonValueKind.Object)
                {
                    return new Contenido(Texto(bulto, "estado"), Texto(bulto, "estadoAnterior"),
                        Texto(bulto, "codigo"), Texto(bulto, "motivoNoEntrega"), VersionesAvisos.V2);
                }

                raiz.TryGetProperty("envio", out var envio);
                return new Contenido(Texto(envio, "estado"), Texto(envio, "estadoAnterior"), null, null,
                    VersionesAvisos.V2);
            }

            return new Contenido(Texto(raiz, "Estado"), Texto(raiz, "EstadoAnterior"), null, null, VersionesAvisos.V1);
        }
        catch (JsonException)
        {
            return new Contenido(null, null, null, null, VersionesAvisos.V1);
        }

        static string? Texto(JsonElement raiz, string nombre) =>
            raiz.ValueKind == JsonValueKind.Object
            && raiz.TryGetProperty(nombre, out var valor)
            && valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;
    }

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
