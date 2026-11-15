using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Envios.Application;

public class DevolucionesService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<DevolucionComercioDto>> ListarAsync(bool soloPendientes, CancellationToken ct = default) =>
        await (
                from e in db.Envios.AsNoTracking()
                join d in db.Devoluciones on e.Id equals d.EnvioId
                join dest in db.Destinatarios on e.DestinatarioId equals dest.Id
                where e.Estado == EstadoEnvio.EnDevolucion || (!soloPendientes && e.Estado == EstadoEnvio.Devuelto)
                orderby d.FechaInicio descending
                select new DevolucionComercioDto(e.Id, e.CodigoSeguimiento, e.ReferenciaExterna,
                    dest.Nombre + " " + dest.Apellido, e.Estado.ToString(), d.Motivo, d.FechaInicio, d.FechaFinalizacion))
            .Take(200)
            .ToListAsync(ct);

    public async Task<bool> ConfirmarRecibidaAsync(int envioId, string? responsableId, CancellationToken ct = default)
    {
        var envio = await db.Envios
            .Include(e => e.Bultos)
            .Include(e => e.IntentosEntrega)
            .Include(e => e.Devolucion)
            .FirstOrDefaultAsync(e => e.Id == envioId, ct);

        if (envio is null)
            return false;

        if (envio.Estado != EstadoEnvio.EnDevolucion)
            throw new TransicionInvalidaException("Solo se confirma la recepción de un envío que está en devolución.");

        envio.CambiarEstado(EstadoEnvio.Devuelto, "Comercio", "El comercio confirmó que recibió la devolución.",
            responsableId, await db.ReglaDelEnvioAsync(envio, ct));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TransicionInvalidaException("El envío cambió al mismo tiempo. Volvé a cargar la lista.");
        }

        return true;
    }
}
