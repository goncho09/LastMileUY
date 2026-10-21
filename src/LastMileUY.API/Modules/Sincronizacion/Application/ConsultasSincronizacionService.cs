using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Sincronizacion.Domain;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Sincronizacion.Application;

// Lo que se consulta de la sincronización: los motivos de no entrega (para la app),
// el historial de operaciones recibidas (para el Backoffice, y el propio para cada repartidor)
// y las imágenes de evidencia (para el Backoffice). El operador lo filtran EF y RLS.
public class ConsultasSincronizacionService(ApplicationDbContext db, IAlmacenEvidencias almacen)
{
    public const int TamanioPaginaMaximo = 100;

    public async Task<IReadOnlyList<MotivoNoEntregaDto>> ListarMotivosAsync(CancellationToken cancellationToken = default) =>
        await db.MotivosNoEntrega.AsNoTracking()
            .Where(m => m.Activo)
            .OrderBy(m => m.Nombre)
            .Select(m => new MotivoNoEntregaDto(m.Id, m.Nombre,m.Descripcion))
            .ToListAsync(cancellationToken);

    // Las más recientes primero. rutaId y resultado son opcionales.
    // usuarioId: solo las que mandó ese repartidor (la app muestra únicamente las propias).
    public async Task<PaginaDto<OperacionSincronizadaDto>> ListarOperacionesAsync(
        int pagina,
        int tamanioPagina,
        int? rutaId,
        string? resultado,
        CancellationToken cancellationToken = default,
        string? usuarioId = null)
    {
        pagina = Math.Max(1, pagina);
        tamanioPagina = Math.Clamp(tamanioPagina, 1, TamanioPaginaMaximo);

        var operaciones = db.OperacionesSincronizadas.AsNoTracking();

        if (usuarioId is not null)
        {
            operaciones = operaciones.Where(o => o.UsuarioId == usuarioId);
        }

        if (rutaId is not null)
        {
            operaciones = operaciones.Where(o => o.RutaId == rutaId);
        }

        if (!string.IsNullOrWhiteSpace(resultado))
        {
            if (!Enum.TryParse<ResultadoOperacion>(resultado, ignoreCase: true, out var buscado) || !Enum.IsDefined(buscado))
            {
                throw new ArgumentException($"El resultado '{resultado}' no existe.");
            }

            operaciones = operaciones.Where(o => o.Resultado == buscado);
        }

        var total = await operaciones.CountAsync(cancellationToken);

        var enPagina = operaciones
            .OrderByDescending(o => o.FechaRecepcion)
            .ThenByDescending(o => o.Id)
            .Skip((pagina - 1) * tamanioPagina)
            .Take(tamanioPagina);

        var filas = await (
                from o in enPagina
                join u in db.Users on o.UsuarioId equals u.Id into usuarios
                from u in usuarios.DefaultIfEmpty()
                join e in db.Envios on o.EnvioId equals e.Id into envios
                from e in envios.DefaultIfEmpty()
                join p in db.PruebasEntrega on o.IntentoEntregaId equals p.IntentoEntregaId into pruebas
                from p in pruebas.DefaultIfEmpty()
                orderby o.FechaRecepcion descending, o.Id descending
                select new
                {
                    Operacion = o,
                    Repartidor = u == null ? "—" : u.Nombre + " " + u.Apellido,
                    Codigo = e == null ? null : e.CodigoSeguimiento,
                    TieneFirma = p != null && p.FirmaArchivoKey != null,
                    TieneFoto = p != null && p.FotoArchivoKey != null
                })
            .ToListAsync(cancellationToken);

        var items = filas
            .Select(x => new OperacionSincronizadaDto(
                x.Operacion.IdOperacion,
                x.Operacion.Tipo.ToString(),
                x.Operacion.Resultado.ToString(),
                x.Operacion.Mensaje,
                x.Operacion.RutaId,
                x.Operacion.EnvioId,
                x.Codigo,
                x.Repartidor,
                x.Operacion.FechaHoraDispositivo,
                x.Operacion.FechaRecepcion,
                x.Operacion.IntentoEntregaId,
                x.TieneFirma,
                x.TieneFoto))
            .ToList();

        return new PaginaDto<OperacionSincronizadaDto>(items, total, pagina, tamanioPagina);
    }

    // La firma o la foto de una entrega. null si el intento no existe (o es de otro operador)
    // o no tiene esa imagen. ArgumentException: el tipo no es "firma" ni "foto".
    public async Task<Imagen?> ObtenerEvidenciaAsync(
        int intentoEntregaId,
        string tipo,
        CancellationToken cancellationToken = default)
    {
        var esFirma = tipo.Equals("firma", StringComparison.OrdinalIgnoreCase);

        if (!esFirma && !tipo.Equals("foto", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La evidencia tiene que ser firma o foto.");
        }

        var prueba = await db.PruebasEntrega.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IntentoEntregaId == intentoEntregaId, cancellationToken);

        var clave = esFirma ? prueba?.FirmaArchivoKey : prueba?.FotoArchivoKey;

        return clave is null ? null : await almacen.LeerAsync(clave, cancellationToken);
    }
}
