using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Application;

public static class BloqueoRepartidor
{
    public static async Task<Repartidor?> ObtenerAsync(
        ApplicationDbContext db,
        int repartidorId,
        CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "El bloqueo requiere una transacción activa.");

        return await db.Repartidores
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM "Repartidores"
                                  WHERE "Id" = {repartidorId}
                                  FOR UPDATE
                                  """)
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }
}