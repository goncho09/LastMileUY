using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Mensajeria.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Mensajeria.Application;

public sealed class ReintentosMensajesFallidosService(
    ApplicationDbContext db)
{
    
    public async Task<bool> SolicitarAsync(
        Guid id,
        string administradorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(administradorId))
            throw new ArgumentException("Administrador inválido.");

        await using var transaccion =
            await db.Database.BeginTransactionAsync(ct);

        var ahora = DateTime.UtcNow;

        // Solo un administrador puede solicitar el reintento.
        var actualizados = await db.MensajesFallidos
            .Where(x =>
                x.Id == id &&
                x.Estado == EstadoMensajeFallido.Pendiente)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Estado,
                    EstadoMensajeFallido.ReprocesamientoSolicitado)
                .SetProperty(
                    x => x.FechaSolicitudReproceso,
                    (DateTime?)ahora)
                .SetProperty(
                    x => x.SolicitadoPorId,
                    administradorId), ct);

        if (actualizados == 0)
        {
            await transaccion.RollbackAsync(ct);
            return false;
        }

        var mensaje = await db.MensajesFallidos
            .AsNoTracking()
            .SingleAsync(x => x.Id == id, ct);

        var outbox = new MensajeReintentoOutbox
        {
            MensajeFallidoId = mensaje.Id,
            MensajeId = mensaje.MensajeId,
            Contenido = mensaje.Contenido,
            Tipo = mensaje.Tipo,
            CorrelacionId = mensaje.CorrelacionId,
            FechaCreacion = ahora
        };

        db.MensajesReintentoOutbox.Add(outbox);

        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return true;
    }

}