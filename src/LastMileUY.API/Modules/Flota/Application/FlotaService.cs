using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Deposito.Application;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Flota;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Flota.Application;

public class FlotaService(ApplicationDbContext db, TimeProvider reloj)
{
    public const int MaximoPorLote = 200;
    public static readonly TimeSpan Aviso = TimeSpan.FromMinutes(60);

    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");
    private static readonly EstadoRuta[] EnLaCalle = [EstadoRuta.Despachada, EstadoRuta.EnCurso];
    private static readonly EstadoEnvio[] PorEntregar = [EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito];

    public async Task<RegistrarPosicionesResponse?> RegistrarAsync(
        int rutaId, string usuarioId, RegistrarPosicionesRequest request, CancellationToken ct = default)
    {
        var ruta = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                where r.Id == rutaId && rep.UsuarioId == usuarioId
                select r)
            .FirstOrDefaultAsync(ct);

        if (ruta is null)
            return null;

        var posiciones = request.Posiciones ?? [];

        if (posiciones.Count > MaximoPorLote)
            throw new ArgumentException($"Se pueden mandar hasta {MaximoPorLote} posiciones por vez.");

        if (!EnLaCalle.Contains(ruta.Estado))
            throw new ConflictoDepositoException("La ruta no está en la calle: no se registran posiciones.");

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var validas = posiciones
            .Where(p => p.Latitud is >= -90 and <= 90 && p.Longitud is >= -180 and <= 180)
            .Select(p => (p.Latitud, p.Longitud, FechaHora: Utc(p.FechaHora)))
            .Where(p => p.FechaHora <= ahora.AddMinutes(5) && p.FechaHora >= ahora.AddDays(-2))
            .DistinctBy(p => p.FechaHora)
            .ToList();

        var fechas = validas.Select(p => p.FechaHora).ToList();
        var existentes = (await db.Posiciones.AsNoTracking()
                .Where(p => p.RutaId == rutaId && fechas.Contains(p.FechaHora))
                .Select(p => p.FechaHora)
                .ToListAsync(ct))
            .ToHashSet();

        var nuevas = validas.Where(p => !existentes.Contains(p.FechaHora)).ToList();

        db.Posiciones.AddRange(nuevas.Select(p => new Posicion
        {
            RutaId = rutaId,
            Latitud = Math.Round(p.Latitud, 6),
            Longitud = Math.Round(p.Longitud, 6),
            FechaHora = p.FechaHora,
            FechaRecepcion = ahora
        }));

        await db.SaveChangesAsync(ct);
        return new RegistrarPosicionesResponse(nuevas.Count, posiciones.Count - nuevas.Count);
    }

    public async Task<FlotaDto> ObtenerAsync(CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var rutas = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                join v in db.Vehiculos on r.VehiculoId equals v.Id
                where EnLaCalle.Contains(r.Estado)
                orderby r.Fecha, r.Nombre
                select new
                {
                    r.Id,
                    r.Nombre,
                    r.Fecha,
                    r.Estado,
                    Repartidor = u.Nombre + " " + u.Apellido,
                    v.Matricula,
                    Ultima = db.Posiciones.Where(p => p.RutaId == r.Id).OrderByDescending(p => p.FechaHora).FirstOrDefault()
                })
            .ToListAsync(ct);

        var rutaIds = rutas.Select(r => r.Id).ToList();

        var paradas = await (
                from p in db.Paradas.AsNoTracking()
                join e in db.Envios on p.EnvioId equals e.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                where rutaIds.Contains(p.RutaId)
                select new
                {
                    p.RutaId,
                    e.Id,
                    e.CodigoSeguimiento,
                    e.Estado,
                    e.FechaCreacion,
                    e.FranjaHorariaId,
                    e.ReglaOperativaId,
                    Destinatario = d.Nombre + " " + d.Apellido
                })
            .ToListAsync(ct);

        var franjaIds = paradas.Where(p => p.FranjaHorariaId is not null).Select(p => p.FranjaHorariaId!.Value).Distinct().ToList();
        var franjas = await db.FranjasHorarias.AsNoTracking()
            .Where(f => franjaIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var reglaIds = paradas.Where(p => p.ReglaOperativaId is not null).Select(p => p.ReglaOperativaId!.Value).Distinct().ToList();
        var plazos = await db.ReglasOperativas.AsNoTracking()
            .Where(r => reglaIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.PlazoEntregaHoras, ct);

        var enRiesgo = new List<EnvioEnRiesgoDto>();

        foreach (var parada in paradas.Where(p => PorEntregar.Contains(p.Estado)))
        {
            var ruta = rutas.First(r => r.Id == parada.RutaId);
            var franja = parada.FranjaHorariaId is int franjaId ? franjas.GetValueOrDefault(franjaId) : null;
            int? plazo = parada.ReglaOperativaId is int reglaId && plazos.TryGetValue(reglaId, out var horas) ? horas : null;

            if (Limite(ruta.Fecha, franja, parada.FechaCreacion, plazo) is not { } calculado)
                continue;

            var (limite, motivo) = calculado;

            if (limite - ahora <= Aviso)
            {
                enRiesgo.Add(new EnvioEnRiesgoDto(parada.Id, parada.CodigoSeguimiento, ruta.Id, ruta.Nombre,
                    parada.Destinatario, motivo, limite, limite < ahora));
            }
        }

        return new FlotaDto(
            ahora,
            rutas.Select(r => new VehiculoEnRutaDto(
                    r.Id, r.Nombre, r.Repartidor, r.Matricula, r.Estado.ToString(),
                    paradas.Count(p => p.RutaId == r.Id),
                    paradas.Count(p => p.RutaId == r.Id && PorEntregar.Contains(p.Estado)),
                    r.Ultima?.Latitud, r.Ultima?.Longitud, r.Ultima?.FechaHora))
                .ToList(),
            enRiesgo.OrderBy(e => e.Limite).ToList());
    }

    public static (DateTime Limite, string Motivo)? Limite(
        DateOnly fechaRuta, LastMileUY.API.Modules.Operadores.Domain.FranjaHoraria? franja, DateTime creado, int? plazoHoras)
    {
        var limites = new List<(DateTime Limite, string Motivo)>();

        if (franja is not null)
        {
            var fin = TimeZoneInfo.ConvertTimeToUtc(fechaRuta.ToDateTime(franja.HoraFin), HoraUruguay);
            limites.Add((fin, $"Franja {franja.Nombre} hasta las {franja.HoraFin.ToString("HH:mm")}"));
        }

        if (plazoHoras is > 0)
            limites.Add((creado.AddHours(plazoHoras.Value), $"Plazo comprometido de {plazoHoras} h"));

        return limites.Count == 0 ? null : limites.MinBy(l => l.Limite);
    }

    private static DateTime Utc(DateTime valor) =>
        AlSegundo(valor.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(valor, DateTimeKind.Utc) : valor.ToUniversalTime());

    private static DateTime AlSegundo(DateTime valor) =>
        new(valor.Ticks - valor.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
