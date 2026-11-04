using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Envios.Application;

// Listado y detalle de envíos para el Backoffice. No filtra por operador a mano:
// de eso se encargan el filtro global de EF y el RLS de PostgreSQL.
public class ConsultasEnviosService(
    ApplicationDbContext db,
    IContextoOperador contextoOperador,
    IHttpContextAccessor httpContextAccessor)
{
    public const int TamanioPaginaMaximo = 100;

    public async Task<PaginaDto<EnvioResumenDto>> ListarAsync(
        int pagina,
        int tamanioPagina,
        string? estado,
        string? buscar,
        string? canal = null,
        CancellationToken cancellationToken = default)
    {
        tamanioPagina = Math.Clamp(tamanioPagina, 1, TamanioPaginaMaximo);
        // Con tope: (pagina - 1) * tamanioPagina no puede pasarse de un int. Más allá, la página viene vacía
        pagina = Math.Clamp(pagina, 1, int.MaxValue / tamanioPagina);

        var consulta =
            from e in db.Envios.AsNoTracking()
            join c in db.Comercios on e.ComercioId equals c.Id
            join d in db.Destinatarios on e.DestinatarioId equals d.Id
            select new { Envio = e, Comercio = c.Nombre, Destinatario = d.Nombre + " " + d.Apellido };
        
        var comercioId = ObtenerComercioAutorizado();

        if (comercioId.HasValue)
        {
            consulta = consulta.Where(x =>
                x.Envio.ComercioId == comercioId.Value);
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (!Enum.TryParse<EstadoEnvio>(estado, ignoreCase: true, out var estadoBuscado)
                || !Enum.IsDefined(estadoBuscado))
            {
                throw new ArgumentException($"El estado '{estado}' no existe.");
            }

            consulta = consulta.Where(x => x.Envio.Estado == estadoBuscado);
        }

        if (!string.IsNullOrWhiteSpace(canal))
        {
            if (!Enum.TryParse<CanalIngreso>(canal, ignoreCase: true, out var canalBuscado)
                || !Enum.IsDefined(canalBuscado))
            {
                throw new ArgumentException($"El canal '{canal}' no existe.");
            }

            consulta = consulta.Where(x => x.Envio.Canal == canalBuscado);
        }

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{EscaparLike(buscar.Trim())}%";

            consulta = consulta.Where(x =>
                EF.Functions.ILike(x.Envio.CodigoSeguimiento, patron) ||
                EF.Functions.ILike(x.Envio.ReferenciaExterna ?? "", patron) ||
                EF.Functions.ILike(x.Destinatario, patron));
        }

        var total = await consulta.CountAsync(cancellationToken);

        var items = await consulta
            .OrderByDescending(x => x.Envio.FechaCreacion)
            .ThenByDescending(x => x.Envio.Id)
            .Skip((pagina - 1) * tamanioPagina)
            .Take(tamanioPagina)
            .Select(x => new EnvioResumenDto(
                x.Envio.Id,
                x.Envio.CodigoSeguimiento,
                x.Envio.ReferenciaExterna,
                x.Comercio,
                x.Destinatario,
                x.Envio.DireccionEntrega.Ciudad,
                x.Envio.DireccionEntrega.Departamento,
                x.Envio.Estado.ToString(),
                x.Envio.Modalidad.ToString(),
                x.Envio.Bultos.Count,
                x.Envio.FechaCreacion)
            {
                Canal = x.Envio.Canal.ToString()
            })
            .ToListAsync(cancellationToken);

        return new PaginaDto<EnvioResumenDto>(items, total, pagina, tamanioPagina);
    }

    // Devuelve null si el envío no existe o es de otro operador (para el que pregunta, es lo mismo)
    public async Task<EnvioDetalleDto?> ObtenerDetalleAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var comercioId = ObtenerComercioAutorizado();
        
        var fila = await (
                from e in db.Envios.AsNoTracking()
                join c in db.Comercios on e.ComercioId equals c.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                where e.Id == id &&
                      (!comercioId.HasValue ||
                       e.ComercioId == comercioId.Value)
                select new
                {
                    Envio = e,
                    Comercio = c.Nombre,
                    Destinatario = new DestinatarioDto(d.Nombre, d.Apellido, d.Telefono, d.Email),
                    Bultos = e.Bultos
                        .OrderBy(b => b.Id)
                        .Select(b => new { Dto = new BultoDto(b.Codigo, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm, b.Descripcion), b.Estado })
                        .ToList(),
                    Eventos = e.Eventos
                        .OrderBy(ev => ev.FechaHora)
                        .Select(ev => new EventoEnvioDto(ev.FechaHora, ev.Tipo, ev.Descripcion, ev.Origen))
                        .ToList(),
                    Intentos = e.IntentosEntrega.ToList()
                })
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (fila is null)
        {
            return null;
        }

        var envio = fila.Envio;
        var direccion = envio.DireccionEntrega;

        // Para que EstadosSiguientes e IntentosFallidos cuenten los intentos reales
        foreach (var intento in fila.Intentos)
        {
            envio.IntentosEntrega.Add(intento);
        }

        var regla = await db.ObtenerVigenteAsync(envio.Modalidad, cancellationToken);

        return new EnvioDetalleDto(
            envio.Id,
            envio.CodigoSeguimiento,
            envio.ReferenciaExterna,
            fila.Comercio,
            envio.Estado.ToString(),
            envio.Modalidad.ToString(),
            envio.FechaCreacion,
            envio.FechaEntrega,
            envio.CostoEnvio,
            fila.Destinatario,
            new DireccionDto(
                direccion.Calle,
                direccion.Numero,
                direccion.Apartamento,
                direccion.Ciudad,
                direccion.Departamento,
                direccion.CodigoPostal,
                direccion.Referencia),
            fila.Bultos.Select(b => b.Dto with { Estado = b.Estado.ToString() }).ToList(),
            fila.Eventos)
        {
            EstadosSiguientes = envio.EstadosSiguientes(regla).Select(e => e.ToString()).ToList(),
            IntentosFallidos = envio.IntentosFallidos,
            Canal = envio.Canal.ToString(),
            Regla = regla is null
                ? null
                : new ReglaAplicadaDto(
                    regla.Nombre,
                    regla.MaximoIntentosEntrega,
                    regla.HorasEntreIntentos,
                    regla.PlazoEntregaHoras,
                    regla.RequiereFirma,
                    regla.RequiereFotografia,
                    regla.RequiereDocumentoReceptor),
            FechaLimiteEntrega = regla is null ? null : envio.FechaCreacion.AddHours(regla.PlazoEntregaHoras),
            Intentos = await IntentosAsync(envio.Id, cancellationToken)
        };
    }

    // Cada intento con el resultado de sus bultos y su prueba de entrega. La prueba respalda solo los
    // bultos que se entregaron en ese intento: en una entrega parcial, la firma de quien recibió no
    // cubre los que no se entregaron. Sale de los resultados del mismo intento (no se guarda aparte),
    // así no puede quedar una prueba asociada a un bulto que el intento no entregó.
    private async Task<IReadOnlyList<IntentoEntregaDto>> IntentosAsync(int envioId, CancellationToken cancellationToken)
    {
        var intentos = await db.IntentosEntrega.AsNoTracking()
            .Where(i => i.EnvioId == envioId)
            .OrderBy(i => i.NumeroIntento)
            .Select(i => new
            {
                i.Id,
                i.NumeroIntento,
                i.FechaHora,
                i.Exitoso,
                i.Parcial,
                Motivo = db.MotivosNoEntrega.Where(m => m.Id == i.MotivoNoEntregaId).Select(m => m.Nombre).FirstOrDefault(),
                i.Observaciones,
                Bultos = i.ResultadosBultos
                    .OrderBy(r => r.BultoId)
                    .Select(r => new ResultadoBultoDto(
                        r.Bulto.Codigo,
                        r.Entregado,
                        db.MotivosNoEntrega.Where(m => m.Id == r.MotivoNoEntregaId).Select(m => m.Nombre).FirstOrDefault()))
                    .ToList(),
                Prueba = i.PruebaEntrega == null
                    ? null
                    : new
                    {
                        i.PruebaEntrega.NombreReceptor,
                        TieneFirma = i.PruebaEntrega.FirmaArchivoKey != null,
                        TieneFoto = i.PruebaEntrega.FotoArchivoKey != null
                    }
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return intentos
            .Select(i => new IntentoEntregaDto(
                i.Id,
                i.NumeroIntento,
                i.FechaHora,
                i.Exitoso ? "Entregado" : i.Parcial ? "Parcial" : "NoEntregado",
                i.Motivo,
                i.Observaciones,
                i.Bultos,
                i.Prueba is null
                    ? null
                    : new PruebaEntregaDto(
                        i.Prueba.NombreReceptor,
                        i.Prueba.TieneFirma,
                        i.Prueba.TieneFoto,
                        i.Bultos.Where(b => b.Entregado).Select(b => b.Codigo).ToList())))
            .ToList();
    }
    
    
    public async Task<List<ActividadEnvioDto>> ObtenerActividadRecienteAsync(
        CancellationToken cancellationToken = default)
    {
        var comercioId = ObtenerComercioAutorizado();

        var consulta =
            from evento in db.EventosEnvio.AsNoTracking()
            join envio in db.Envios.AsNoTracking()
                on evento.EnvioId equals envio.Id
            select new { Evento = evento, Envio = envio };

        if (comercioId.HasValue)
        {
            consulta = consulta.Where(x =>
                x.Envio.ComercioId == comercioId.Value);
        }

        return await consulta
            .OrderByDescending(x => x.Evento.FechaHora)
            .ThenByDescending(x => x.Evento.Id)
            .Take(10)
            .Select(x => new ActividadEnvioDto(
                x.Evento.Id,
                x.Envio.Id,
                x.Envio.CodigoSeguimiento,
                x.Evento.Tipo,
                x.Evento.Descripcion,
                x.Evento.Origen,
                x.Evento.FechaHora))
            .ToListAsync(cancellationToken);
    }

    
    
    private int? ObtenerComercioAutorizado()
    {
        var usuario = httpContextAccessor.HttpContext?.User;

        if (usuario?.IsInRole("Comercio") != true)
            return null;

        return contextoOperador.ComercioId
               ?? throw new UnauthorizedAccessException(
                   "El usuario de comercio no tiene un comercio asignado.");
    }

    // Que un % o _ escrito por el usuario se busque como texto y no como comodín
    private static string EscaparLike(string texto) =>
        texto.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
