using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.Contracts.Entregas;
using Microsoft.EntityFrameworkCore;

// Agregá el using de ApplicationDbContext
// que ya utilizás en RegistrarIntentoFallidoService.

namespace LastMileUY.API.Modules.Entregas.Services;

public class ConsultarMotivosNoEntregaService(ApplicationDbContext db)
{
    public async Task<List<MotivoNoEntregaDto>> ListarActivosAsync(
        int operadorId,
        CancellationToken ct = default)
    {
        return await db.Set<MotivoNoEntrega>()
            .AsNoTracking()
            .Where(m =>
                m.OperadorId == operadorId &&
                m.Activo)
            .OrderBy(m => m.Nombre)
            .Select(m => new MotivoNoEntregaDto(
                m.Id,
                m.Nombre,
                m.Descripcion))
            .ToListAsync(ct);
    }
}