using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Entregas.Services;

public class ConfirmarEntregaService(ApplicationDbContext db)
{
    public async Task ConfirmarAsync(
        int envioId,
        int operadorId,
        string usuarioId,
        CancellationToken ct = default)
    {
        var repartidor = await db.Set<Repartidor>()
            .FirstOrDefaultAsync(r =>
                r.UsuarioId == usuarioId &&
                r.OperadorId == operadorId &&
                r.Activo, ct);

        if (repartidor is null)
            throw new UnauthorizedAccessException(
                "No se encontró un repartidor activo.");

        var ruta = await db.Set<Ruta>()
            .FirstOrDefaultAsync(r =>
                r.OperadorId == operadorId &&
                r.RepartidorId == repartidor.Id &&
                Ruta.EstadosParaElRepartidor.Contains(r.Estado) &&
                db.Set<Parada>().Any(p =>
                    p.RutaId == r.Id && p.EnvioId == envioId), ct);

        if (ruta is null)
            throw new KeyNotFoundException(
                "El envío no pertenece a una ruta asignada.");

        var envio = await db.Set<Envio>()
            .Include(e => e.IntentosEntrega)
            .Include(e => e.Eventos)
            .FirstOrDefaultAsync(e =>
                e.Id == envioId &&
                e.OperadorId == operadorId, ct);

        if (envio is null)
            throw new KeyNotFoundException("Envío no encontrado.");

        var ahora = DateTime.UtcNow;

        envio.CambiarEstado(
            EstadoEnvio.Entregado,
            origen: "Repartidor",
            responsableId: usuarioId,
            ocurrido: ahora);

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
            estados.Select(e => e.Id == envioId
                ? EstadoEnvio.Entregado
                : e.Estado));

        await db.SaveChangesAsync(ct);
    }
}