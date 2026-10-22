using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Application;

// Consultas de hojas de ruta: el listado y el detalle para el Backoffice, lo que se puede elegir
// al armar una ruta y la hoja que descarga el repartidor. El operador lo filtran EF y RLS.
public class ConsultasRutasService(ApplicationDbContext db)
{
    public const int TamanioPaginaMaximo = 100;

    public async Task<PaginaDto<RutaResumenDto>> ListarAsync(
        int pagina,
        int tamanioPagina,
        DateOnly? fecha,
        string? estado,
        CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(1, pagina);
        tamanioPagina = Math.Clamp(tamanioPagina, 1, TamanioPaginaMaximo);

        var rutas = db.Rutas.AsNoTracking();

        if (fecha is not null)
        {
            rutas = rutas.Where(r => r.Fecha == fecha);
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (!Enum.TryParse<EstadoRuta>(estado, ignoreCase: true, out var estadoBuscado)
                || !Enum.IsDefined(estadoBuscado))
            {
                throw new ArgumentException($"El estado '{estado}' no existe.");
            }

            rutas = rutas.Where(r => r.Estado == estadoBuscado);
        }

        var total = await rutas.CountAsync(cancellationToken);

        var items = await Resumir(rutas
                .OrderByDescending(r => r.Fecha)
                .ThenByDescending(r => r.Id)
                .Skip((pagina - 1) * tamanioPagina)
                .Take(tamanioPagina))
            .ToListAsync(cancellationToken);

        return new PaginaDto<RutaResumenDto>(items, total, pagina, tamanioPagina);
    }

    // Las rutas despachadas o en curso del repartidor que inició sesión
    public async Task<IReadOnlyList<RutaResumenDto>> ListarDelRepartidorAsync(
        string usuarioId,
        CancellationToken cancellationToken = default)
    {
        var rutas =
            from r in db.Rutas.AsNoTracking()
            join rep in db.Repartidores on r.RepartidorId equals rep.Id
            where rep.UsuarioId == usuarioId && Ruta.EstadosParaElRepartidor.Contains(r.Estado)
            orderby r.Fecha, r.Id
            select r;

        return await Resumir(rutas).ToListAsync(cancellationToken);
    }

    public async Task<RutaDetalleDto?> ObtenerDetalleAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var cabecera = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                join v in db.Vehiculos on r.VehiculoId equals v.Id
                where r.Id == id
                select new
                {
                    Ruta = r,
                    Repartidor = new RepartidorDto(rep.Id, u.Nombre + " " + u.Apellido),
                    Vehiculo = new VehiculoDto(v.Id, v.Matricula, v.Tipo, v.CapacidadPesoKg, v.CapacidadVolumenM3)
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (cabecera is null)
        {
            return null;
        }

        var filas = await (
                from p in db.Paradas.AsNoTracking()
                join e in db.Envios on p.EnvioId equals e.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                join f in db.FranjasHorarias on e.FranjaHorariaId equals f.Id into franjas
                from f in franjas.DefaultIfEmpty()
                where p.RutaId == id
                orderby p.Orden
                select new
                {
                    p.Orden,
                    Envio = e,
                    Destinatario = new DestinatarioDto(d.Nombre, d.Apellido, d.Telefono, d.Email),
                    Franja = f == null ? null : new FranjaHorariaDto(f.Nombre, f.HoraInicio, f.HoraFin),
                    CantidadBultos = e.Bultos.Count(),
                    PesoKg = e.Bultos.Sum(b => (decimal?)b.PesoKg) ?? 0,
                    VolumenM3 = e.Bultos.Sum(b => (decimal?)(b.LargoCm * b.AnchoCm * b.AltoCm)) ?? 0
                })
            .ToListAsync(cancellationToken);

        // La evidencia que pide cada parada sale de la regla vigente para la modalidad de su envío
        var evidencias = await db.ReglasOperativas.AsNoTracking()
            .Where(r => r.Activa)
            .ToDictionaryAsync(
                r => r.Modalidad,
                r => new EvidenciaRequeridaDto(r.RequiereFirma, r.RequiereFotografia, r.RequiereDocumentoReceptor),
                cancellationToken);

        var paradas = filas
            .Select(x => new ParadaDto(
                x.Orden,
                x.Envio.Id,
                x.Envio.CodigoSeguimiento,
                x.Envio.Estado.ToString(),
                x.Destinatario,
                ADto(x.Envio.DireccionEntrega),
                x.Franja,
                x.CantidadBultos,
                x.PesoKg,
                x.VolumenM3 / Bulto.CentimetrosCubicosPorMetroCubico,
                x.Envio.Modalidad.ToString(),
                evidencias.GetValueOrDefault(x.Envio.Modalidad, EvidenciaRequeridaDto.Ninguna)))
            .ToList();

        var ruta = cabecera.Ruta;

        return new RutaDetalleDto(
            ruta.Id,
            ruta.Nombre,
            ruta.Fecha,
            ruta.Estado.ToString(),
            cabecera.Repartidor,
            cabecera.Vehiculo,
            paradas.Sum(p => p.PesoKg),
            paradas.Sum(p => p.VolumenM3),
            paradas);
    }

    // La hoja de ruta para la app del repartidor: solo la del repartidor asignado y solo
    // una vez despachada (antes el Backoffice todavía la puede estar armando).
    public async Task<(ResultadoHoja Resultado, RutaDetalleDto? Hoja)> ObtenerHojaDelRepartidorAsync(
        int id,
        string usuarioId,
        CancellationToken cancellationToken = default)
    {
        var ruta = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                where r.Id == id
                select new { r.Estado, rep.UsuarioId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ruta is null)
        {
            return (ResultadoHoja.NoExiste, null);
        }

        if (ruta.UsuarioId != usuarioId)
        {
            return (ResultadoHoja.DeOtroRepartidor, null);
        }

        if (!Ruta.EstadosParaElRepartidor.Contains(ruta.Estado))
        {
            return (ResultadoHoja.NoDespachada, null);
        }

        return (ResultadoHoja.Disponible, await ObtenerHojaParaLaAppAsync(id, cancellationToken));
    }

    // Repartidores y vehículos activos, y los envíos que se pueden sumar a una ruta nueva
    public async Task<OpcionesRutaDto> ObtenerOpcionesAsync(CancellationToken cancellationToken = default)
    {
        var repartidores = await (
                from rep in db.Repartidores.AsNoTracking()
                join u in db.Users on rep.UsuarioId equals u.Id
                where rep.Activo && u.Activo
                orderby u.Nombre, u.Apellido
                select new RepartidorDto(rep.Id, u.Nombre + " " + u.Apellido))
            .ToListAsync(cancellationToken);

        var vehiculos = await db.Vehiculos.AsNoTracking()
            .Where(v => v.Activo)
            .OrderBy(v => v.Matricula)
            .Select(v => new VehiculoDto(v.Id, v.Matricula, v.Tipo, v.CapacidadPesoKg, v.CapacidadVolumenM3))
            .ToListAsync(cancellationToken);

        var enRutasAbiertas =
            from p in db.Paradas
            join r in db.Rutas on p.RutaId equals r.Id
            where Ruta.EstadosAbiertos.Contains(r.Estado)
            select p.EnvioId;

        var filas = await (
                from e in db.Envios.AsNoTracking()
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                join f in db.FranjasHorarias on e.FranjaHorariaId equals f.Id into franjas
                from f in franjas.DefaultIfEmpty()
                where (e.Estado == EstadoEnvio.EnDeposito || e.Estado == EstadoEnvio.Reprogramado)
                      && !enRutasAbiertas.Contains(e.Id)
                orderby f == null ? 1 : 0, f.HoraInicio, e.Id
                select new
                {
                    Envio = e,
                    Destinatario = d.Nombre + " " + d.Apellido,
                    Franja = f == null ? null : new FranjaHorariaDto(f.Nombre, f.HoraInicio, f.HoraFin),
                    PesoKg = e.Bultos.Sum(b => (decimal?)b.PesoKg) ?? 0,
                    VolumenM3 = e.Bultos.Sum(b => (decimal?)(b.LargoCm * b.AnchoCm * b.AltoCm)) ?? 0
                })
            .ToListAsync(cancellationToken);

        var envios = filas
            .Select(x => new EnvioDisponibleDto(
                x.Envio.Id,
                x.Envio.CodigoSeguimiento,
                x.Envio.Estado.ToString(),
                x.Destinatario,
                $"{x.Envio.DireccionEntrega.Calle} {x.Envio.DireccionEntrega.Numero}, {x.Envio.DireccionEntrega.Ciudad}",
                x.Franja,
                x.PesoKg,
                x.VolumenM3 / Bulto.CentimetrosCubicosPorMetroCubico))
            .ToList();

        return new OpcionesRutaDto(repartidores, vehiculos, envios);
    }

    private IQueryable<RutaResumenDto> Resumir(IQueryable<Ruta> rutas) =>
        from r in rutas
        join rep in db.Repartidores on r.RepartidorId equals rep.Id
        join u in db.Users on rep.UsuarioId equals u.Id
        join v in db.Vehiculos on r.VehiculoId equals v.Id
        select new RutaResumenDto(
            r.Id,
            r.Nombre,
            r.Fecha,
            r.Estado.ToString(),
            u.Nombre + " " + u.Apellido,
            v.Matricula,
            r.Paradas.Count());

    private static DireccionDto ADto(DireccionEntrega d) =>
        new(d.Calle, d.Numero, d.Apartamento, d.Ciudad, d.Departamento, d.CodigoPostal, d.Referencia);

    // Lo que el repartidor puede abrir desde la app: sus rutas despachadas o en curso, y también
    // las finalizadas (para ver cómo quedó la última). Una planificada todavía se está armando.
    private static readonly EstadoRuta[] EstadosVisiblesEnLaApp =
        [.. Ruta.EstadosParaElRepartidor, EstadoRuta.Finalizada];

    public async Task<RutaDetalleDto?> ObtenerDetalleDelRepartidorAsync(
        int rutaId,
        int operadorId,
        string usuarioId,
        CancellationToken ct = default)
    {
        var esMiRuta = await db.Set<Ruta>()
            .AnyAsync(r =>
                    r.Id == rutaId &&
                    r.OperadorId == operadorId &&
                    EstadosVisiblesEnLaApp.Contains(r.Estado) &&
                    db.Set<Repartidor>().Any(rep =>
                        rep.Id == r.RepartidorId &&
                        rep.UsuarioId == usuarioId &&
                        rep.OperadorId == operadorId),
                ct);

        if (!esMiRuta)
            return null;

        return await ObtenerHojaParaLaAppAsync(rutaId, ct);
    }

    // La hoja que se le manda al celular del repartidor. Lo que llega al celular puede quedar
    // guardado ahí (para trabajar sin conexión), así que va solo lo que necesita para entregar:
    // sin el email del destinatario (para comunicarse alcanza el teléfono).
    public async Task<RutaDetalleDto?> ObtenerHojaParaLaAppAsync(int id, CancellationToken ct = default) =>
        SinDatosInnecesarios(await ObtenerDetalleAsync(id, ct));

    private static RutaDetalleDto? SinDatosInnecesarios(RutaDetalleDto? hoja) =>
        hoja is null
            ? null
            : hoja with
            {
                Paradas = hoja.Paradas
                    .Select(p => p with { Destinatario = p.Destinatario with { Email = null } })
                    .ToList()
            };
}

public enum ResultadoHoja
{
    Disponible,
    NoExiste,
    DeOtroRepartidor,
    NoDespachada
}
