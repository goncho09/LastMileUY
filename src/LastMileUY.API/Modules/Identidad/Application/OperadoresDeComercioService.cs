using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Identidad.Application;

public class OperadoresDeComercioService(ApplicationDbContext db)
{
    public async Task<List<OperadorDisponibleDto>> ListarAsync(
        int comercioId,
        CancellationToken cancellationToken = default)
    {
        return await (
                from oc in db.OperadoresComercios.AsNoTracking()
                join o in db.Operadores.AsNoTracking() on oc.OperadorId equals o.Id
                where oc.ComercioId == comercioId && oc.Activo && o.Activo && !o.EsAmbientePruebas
                orderby o.Nombre
                select new OperadorDisponibleDto(o.Id, o.Nombre))
            .ToListAsync(cancellationToken);
    }
}
