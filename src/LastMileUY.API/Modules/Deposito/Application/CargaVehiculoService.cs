using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Deposito.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Deposito;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Deposito.Application;

public class CargaVehiculoService(ApplicationDbContext db, TimeProvider reloj)
{
    public async Task<IReadOnlyList<RutaPorCargarDto>> PorCargarAsync(CancellationToken ct = default)
    {
        var rutas = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                join v in db.Vehiculos on r.VehiculoId equals v.Id
                where r.Estado == EstadoRuta.Planificada
                orderby r.Fecha, r.Nombre
                select new
                {
                    r.Id,
                    r.Nombre,
                    r.Fecha,
                    Repartidor = u.Nombre + " " + u.Apellido,
                    v.Matricula,
                    Envios = r.Paradas.Count,
                    Bultos = r.Paradas.SelectMany(p => p.Bultos).Count()
                })
            .ToListAsync(ct);

        var resultado = new List<RutaPorCargarDto>();

        foreach (var ruta in rutas)
        {
            resultado.Add(new RutaPorCargarDto(ruta.Id, ruta.Nombre, ruta.Fecha, ruta.Repartidor, ruta.Matricula,
                ruta.Envios, ruta.Bultos, await EstadoCargaAsync(ruta.Id, ct)));
        }

        return resultado;
    }

    public async Task<CargaRutaDto?> ObtenerAsync(int rutaId, CancellationToken ct = default)
    {
        var ruta = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                join v in db.Vehiculos on r.VehiculoId equals v.Id
                where r.Id == rutaId
                select new { r.Id, r.Nombre, r.Fecha, r.Estado, Repartidor = u.Nombre + " " + u.Apellido, v.Matricula })
            .FirstOrDefaultAsync(ct);

        if (ruta is null)
            return null;

        var ultima = await UltimaVerificacionAsync(rutaId, ct);
        var cargados = ultima?.Bultos.ToDictionary(b => b.BultoId, b => b.Cargado) ?? [];

        var bultos = await BultosDeLaRutaAsync(rutaId, ct);

        return new CargaRutaDto(
            ruta.Id,
            ruta.Nombre,
            ruta.Fecha,
            ruta.Estado.ToString(),
            ruta.Repartidor,
            ruta.Matricula,
            Estado(ultima, bultos.Select(b => b.BultoId)),
            ultima?.FechaHora,
            bultos
                .Select(b => new BultoACargarDto(b.Codigo, b.EnvioId, b.CodigoSeguimiento, b.Orden, b.Destinatario,
                    b.Descripcion, cargados.TryGetValue(b.BultoId, out var cargado) ? cargado : null))
                .ToList())
        {
            Sobrantes = ultima?.Sobrantes ?? [],
            OrigenUltimaVerificacion = ultima?.Origen
        };
    }

    public async Task<CargaRutaDto?> VerificarAsync(
        int rutaId,
        VerificarCargaRequest request,
        string? responsableId,
        CancellationToken ct = default)
    {
        var ruta = await db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rutaId, ct);

        if (ruta is null)
            return null;

        if (ruta.Estado != EstadoRuta.Planificada)
            throw new ConflictoDepositoException("Solo se verifica la carga de una ruta que todavía no salió.");

        await GuardarVerificacionAsync(ruta, request, responsableId, OrigenesVerificacion.Deposito, ct);
        return await ObtenerAsync(rutaId, ct);
    }

    public async Task<CargaRutaDto?> VerificarDelRepartidorAsync(
        int rutaId,
        string usuarioId,
        VerificarCargaRequest request,
        CancellationToken ct = default)
    {
        var ruta = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                where r.Id == rutaId && rep.UsuarioId == usuarioId
                select r)
            .FirstOrDefaultAsync(ct);

        if (ruta is null)
            return null;

        if (ruta.Estado is not (EstadoRuta.Despachada or EstadoRuta.EnCurso))
            throw new ConflictoDepositoException("La ruta ya terminó: no se puede registrar la carga.");

        await GuardarVerificacionAsync(ruta, request, usuarioId, OrigenesVerificacion.Repartidor, ct);
        return await ObtenerAsync(rutaId, ct);
    }

    private async Task GuardarVerificacionAsync(
        Ruta ruta,
        VerificarCargaRequest request,
        string? responsableId,
        string origen,
        CancellationToken ct)
    {
        var observaciones = string.IsNullOrWhiteSpace(request.Observaciones) ? null : request.Observaciones.Trim();

        if (observaciones?.Length > 500)
            throw new ArgumentException("Las observaciones pueden tener hasta 500 caracteres.");

        var escaneados = (request.BultosCargados ?? [])
            .Select(c => c?.Trim().ToUpperInvariant() ?? "")
            .Where(c => c.Length > 0)
            .ToHashSet();

        if (escaneados.Count > 500 || escaneados.Any(c => c.Length > 40))
            throw new ArgumentException("Hay demasiados códigos o alguno es demasiado largo.");

        var bultos = await BultosDeLaRutaAsync(ruta.Id, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var enElDispositivo = request.VerificadoEn is { } cuando
            ? (cuando.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(cuando, DateTimeKind.Utc) : cuando.ToUniversalTime())
            : ahora;

        var verificacion = new VerificacionCarga
        {
            OperadorId = ruta.OperadorId,
            RutaId = ruta.Id,
            FechaHora = enElDispositivo < ahora ? enElDispositivo : ahora,
            ResponsableId = responsableId,
            Observaciones = observaciones,
            Origen = origen,
            Completa = bultos.Count > 0 && bultos.All(b => escaneados.Contains(b.Codigo)),
            Sobrantes = escaneados.Where(c => bultos.All(b => b.Codigo != c)).Order().ToList(),
            Bultos = bultos
                .Select(b => new VerificacionCargaBulto { BultoId = b.BultoId, Cargado = escaneados.Contains(b.Codigo) })
                .ToList()
        };

        db.VerificacionesCarga.Add(verificacion);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> QuitarEnvioAsync(int rutaId, int envioId, string? responsableId, CancellationToken ct = default)
    {
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var ruta = await db.Rutas
            .Include(r => r.Paradas)
            .ThenInclude(p => p.Bultos)
            .FirstOrDefaultAsync(r => r.Id == rutaId, ct);

        var parada = ruta?.Paradas.FirstOrDefault(p => p.EnvioId == envioId);

        if (ruta is null || parada is null)
            return false;

        if (ruta.Estado != EstadoRuta.Planificada)
            throw new ConflictoDepositoException("La ruta ya salió: no se le pueden bajar envíos.");

        var envio = await db.Envios.FirstAsync(e => e.Id == envioId, ct);

        try
        {
            envio.CambiarEstado(EstadoEnvio.EnDeposito, "Deposito",
                $"Se bajó de la ruta \"{ruta.Nombre}\" al verificar la carga.", responsableId);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictoDepositoException(ex.Message);
        }

        db.BultosParada.RemoveRange(parada.Bultos);
        db.Paradas.Remove(parada);
        ruta.Paradas.Remove(parada);

        var restantes = ruta.Paradas.OrderBy(p => p.Orden).ToList();

        foreach (var otra in restantes)
            otra.Orden = -otra.Orden;

        try
        {
            await db.SaveChangesAsync(ct);

            for (var i = 0; i < restantes.Count; i++)
                restantes[i].Orden = i + 1;

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictoDepositoException("Otra persona cambió el envío o la ruta al mismo tiempo. Volvé a cargarla.");
        }

        await transaccion.CommitAsync(ct);
        return true;
    }

    public async Task<string> EstadoCargaAsync(int rutaId, CancellationToken ct = default)
    {
        var ultima = await UltimaVerificacionAsync(rutaId, ct);
        var bultos = await db.BultosParada.AsNoTracking()
            .Where(bp => db.Paradas.Any(p => p.Id == bp.ParadaId && p.RutaId == rutaId))
            .Select(bp => bp.BultoId)
            .ToListAsync(ct);

        return Estado(ultima, bultos);
    }

    private static string Estado(VerificacionCarga? ultima, IEnumerable<int> bultosDeLaRuta)
    {
        if (ultima is null)
            return EstadosCarga.SinVerificar;

        var cargados = ultima.Bultos.Where(b => b.Cargado).Select(b => b.BultoId).ToHashSet();
        var todos = bultosDeLaRuta.ToList();

        return todos.Count > 0 && todos.All(cargados.Contains) ? EstadosCarga.Verificada : EstadosCarga.Incompleta;
    }

    private Task<VerificacionCarga?> UltimaVerificacionAsync(int rutaId, CancellationToken ct) =>
        db.VerificacionesCarga.AsNoTracking()
            .Include(v => v.Bultos)
            .Where(v => v.RutaId == rutaId)
            .OrderByDescending(v => v.FechaHora)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<List<BultoDeRuta>> BultosDeLaRutaAsync(int rutaId, CancellationToken ct) =>
        await (
                from p in db.Paradas.AsNoTracking()
                join bp in db.BultosParada on p.Id equals bp.ParadaId
                join b in db.Bultos on bp.BultoId equals b.Id
                join e in db.Envios on p.EnvioId equals e.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                where p.RutaId == rutaId
                orderby p.Orden, b.Id
                select new BultoDeRuta(b.Id, b.Codigo, e.Id, e.CodigoSeguimiento, p.Orden, d.Nombre + " " + d.Apellido, b.Descripcion))
            .ToListAsync(ct);

    private sealed record BultoDeRuta(
        int BultoId, string Codigo, int EnvioId, string CodigoSeguimiento, int Orden, string Destinatario, string? Descripcion);
}
