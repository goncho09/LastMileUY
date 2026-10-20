using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Entregas.Application;

public class RegistrarIntentoFallidoService(ApplicationDbContext db)
{
    public async Task RegistrarAsync(
        int envioId,
        int motivoNoEntregaId,
        string? observaciones,
        int operadorId,
        string usuarioId,
        CancellationToken ct = default)
    {
        if (observaciones?.Length > 1000)
            throw new ArgumentException(
                "Las observaciones no pueden superar los 1000 caracteres.");

        var repartidor = await db.Set<Repartidor>()
            .FirstOrDefaultAsync(r =>
                r.UsuarioId == usuarioId &&
                r.OperadorId == operadorId &&
                r.Activo,
                ct);

        if (repartidor is null)
            throw new UnauthorizedAccessException(
                "No se encontró un repartidor activo.");

        var motivo = await db.Set<MotivoNoEntrega>()
            .FirstOrDefaultAsync(m =>
                m.Id == motivoNoEntregaId &&
                m.OperadorId == operadorId &&
                m.Activo,
                ct);

        if (motivo is null)
            throw new ArgumentException(
                "El motivo de no entrega no existe o no está activo.");

        var ruta = await db.Set<Ruta>()
            .FirstOrDefaultAsync(r =>
                r.OperadorId == operadorId &&
                r.RepartidorId == repartidor.Id &&
                Ruta.EstadosParaElRepartidor.Contains(r.Estado) &&
                db.Set<Parada>().Any(p =>
                    p.RutaId == r.Id &&
                    p.EnvioId == envioId),
                ct);

        if (ruta is null)
            throw new KeyNotFoundException(
                "El envío no pertenece a una ruta asignada.");

        var parada = await db.Set<Parada>()
            .FirstAsync(p =>
                p.RutaId == ruta.Id &&
                p.EnvioId == envioId,
                ct);

        var envio = await db.Set<Envio>()
            .Include(e => e.IntentosEntrega)
            .Include(e => e.Eventos)
            .FirstOrDefaultAsync(e =>
                e.Id == envioId &&
                e.OperadorId == operadorId,
                ct);

        if (envio is null)
            throw new KeyNotFoundException("Envío no encontrado.");

        var regla = await db.ObtenerVigenteAsync(
            envio.Modalidad, ct);

        var ahora = DateTime.UtcNow;

        var intento = envio.CambiarEstado(
            EstadoEnvio.NoEntregado,
            origen: "Repartidor",
            descripcion: observaciones?.Trim(),
            responsableId: usuarioId,
            regla: regla,
            ocurrido: ahora);

        if (intento is null)
            throw new InvalidOperationException(
                "No se generó el intento de entrega.");

        intento.MotivoNoEntregaId = motivo.Id;
        intento.ParadaId = parada.Id;

        ruta.RegistrarActividad(ahora);

        var idsEnvios = await db.Set<Parada>()
            .Where(p => p.RutaId == ruta.Id)
            .Select(p => p.EnvioId)
            .ToListAsync(ct);

        var estados = await db.Set<Envio>()
            .Where(e => idsEnvios.Contains(e.Id))
            .Select(e => new { e.Id, e.Estado })
            .ToListAsync(ct);

        ruta.FinalizarSiTermino(
            estados.Select(e =>
                e.Id == envioId
                    ? EstadoEnvio.NoEntregado
                    : e.Estado));

        await db.SaveChangesAsync(ct);
    }
}