using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.Contracts.Reportes;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Reportes.Application;

public class ReportesService(ApplicationDbContext db)
{
    public const int MaximoDias = 366;

    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private static readonly EstadoEnvio[] NoEntregados =
        [EstadoEnvio.NoEntregado, EstadoEnvio.EnDevolucion, EstadoEnvio.Devuelto, EstadoEnvio.Extraviado];

    public sealed record FilaEnvio(
        int Id, int ComercioId, string Comercio, string? Zona, EstadoEnvio Estado,
        DateTime FechaCreacion, DateTime? FechaEntrega, decimal? Costo, int? PlazoHoras);

    public async Task<ReporteGestionDto> ObtenerAsync(DateOnly desde, DateOnly hasta, int? comercioId = null, CancellationToken ct = default)
    {
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");

        if (hasta.DayNumber - desde.DayNumber >= MaximoDias)
            throw new ArgumentException($"El período puede tener como máximo {MaximoDias} días.");

        var inicio = TimeZoneInfo.ConvertTimeToUtc(desde.ToDateTime(TimeOnly.MinValue), HoraUruguay);
        var fin = TimeZoneInfo.ConvertTimeToUtc(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), HoraUruguay);

        var envios = await (
                from e in db.Envios.AsNoTracking()
                join c in db.Comercios on e.ComercioId equals c.Id
                join z in db.Zonas on e.ZonaId equals z.Id into zonas
                from z in zonas.DefaultIfEmpty()
                join r in db.ReglasOperativas on e.ReglaOperativaId equals r.Id into reglas
                from r in reglas.DefaultIfEmpty()
                where e.FechaCreacion >= inicio && e.FechaCreacion < fin
                      && (comercioId == null || e.ComercioId == comercioId)
                select new FilaEnvio(e.Id, e.ComercioId, c.Nombre, z.Nombre, e.Estado,
                    e.FechaCreacion, e.FechaEntrega, e.CostoEnvio, (int?)r.PlazoEntregaHoras))
            .ToListAsync(ct);

        var envioIds = envios.Select(e => e.Id).ToList();

        var intentos = await (
                from i in db.IntentosEntrega.AsNoTracking()
                join p in db.Paradas on i.ParadaId equals p.Id
                join ru in db.Rutas on p.RutaId equals ru.Id
                join rep in db.Repartidores on ru.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                where i.FechaHora >= inicio && i.FechaHora < fin && comercioId == null
                select new { Repartidor = u.Nombre + " " + u.Apellido, i.Exitoso })
            .ToListAsync(ct);

        var motivos = await (
                from i in db.IntentosEntrega.AsNoTracking()
                join m in db.MotivosNoEntrega on i.MotivoNoEntregaId equals m.Id
                where !i.Exitoso && i.FechaHora >= inicio && i.FechaHora < fin
                      && (comercioId == null || envioIds.Contains(i.EnvioId))
                group i by m.Nombre into g
                orderby g.Count() descending
                select new MotivoNoEntregaReporteDto(g.Key, g.Count()))
            .ToListAsync(ct);

        return new ReporteGestionDto(
            desde,
            hasta,
            envios.Count,
            Volumen(envios, desde, hasta),
            Cumplimiento(envios, e => e.Zona ?? "Sin zona"),
            Cumplimiento(envios, e => e.Comercio),
            intentos
                .GroupBy(i => i.Repartidor)
                .Select(g => new EfectividadRepartidorDto(g.Key, g.Count(), g.Count(i => i.Exitoso)))
                .OrderByDescending(r => r.Intentos)
                .ToList(),
            motivos,
            Liquidacion(envios));
    }

    public static bool ATiempo(FilaEnvio envio) =>
        envio is { Estado: EstadoEnvio.Entregado, FechaEntrega: { } entrega }
        && (envio.PlazoHoras is not > 0 || entrega <= envio.FechaCreacion.AddHours(envio.PlazoHoras.Value));

    public static List<CumplimientoDto> Cumplimiento(IEnumerable<FilaEnvio> envios, Func<FilaEnvio, string> clave) =>
        envios
            .GroupBy(clave)
            .Select(g => new CumplimientoDto(
                g.Key,
                g.Count(),
                g.Count(e => e.Estado == EstadoEnvio.Entregado),
                g.Count(ATiempo),
                g.Count(e => NoEntregados.Contains(e.Estado))))
            .OrderByDescending(c => c.Envios)
            .ThenBy(c => c.Nombre)
            .ToList();

    public static List<LiquidacionComercioDto> Liquidacion(IEnumerable<FilaEnvio> envios) =>
        envios
            .GroupBy(e => (e.ComercioId, e.Comercio))
            .Select(g => new LiquidacionComercioDto(
                g.Key.ComercioId,
                g.Key.Comercio,
                g.Count(),
                g.Count(e => e.Estado == EstadoEnvio.Entregado),
                g.Count(e => e.Estado is EstadoEnvio.Devuelto or EstadoEnvio.EnDevolucion),
                g.Count(e => e.Costo is null),
                g.Sum(e => e.Costo ?? 0)))
            .OrderBy(l => l.Comercio)
            .ToList();

    private static List<VolumenDiaDto> Volumen(List<FilaEnvio> envios, DateOnly desde, DateOnly hasta)
    {
        var porDia = envios
            .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(e.FechaCreacion, HoraUruguay)))
            .ToDictionary(g => g.Key, g => (Envios: g.Count(), Entregados: g.Count(e => e.Estado == EstadoEnvio.Entregado)));

        return Enumerable.Range(0, hasta.DayNumber - desde.DayNumber + 1)
            .Select(desde.AddDays)
            .Select(d => porDia.TryGetValue(d, out var v) ? new VolumenDiaDto(d, v.Envios, v.Entregados) : new VolumenDiaDto(d, 0, 0))
            .ToList();
    }
}
