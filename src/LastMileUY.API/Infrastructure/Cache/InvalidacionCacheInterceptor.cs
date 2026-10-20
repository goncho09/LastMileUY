using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Cache;

// Invalidación del caché (ver ADR de caché): cada vez que se guarda algo en la base, mira qué cambió
// y borra del caché lo que quedó viejo. Al estar en un solo lugar, cualquier código que cambie
// envíos, rutas o la configuración del operador invalida solo, aunque no sepa que existe el caché.
//   - Hoja de ruta: cambia una ruta, una parada o el estado de un envío que está en una ruta
//     (sincronización del repartidor, cambio de estado en el Backoffice, armado y despacho).
//   - Configuración del operador: cambia el operador (nombre, color, logo) o una regla operativa.
// Se crea uno por pedido (con su DbContext), por eso puede guardar lo pendiente en campos.
public class InvalidacionCacheInterceptor(CacheDistribuido cache) : SaveChangesInterceptor
{
    private readonly List<Ruta> rutasCambiadas = [];
    private readonly List<Parada> paradasCambiadas = [];
    private readonly HashSet<int> enviosCambiados = [];
    private readonly HashSet<int> operadoresCambiados = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AnotarCambios(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AnotarCambios(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await InvalidarAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        InvalidarAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    // Si el guardado falla no cambió nada: no hay que invalidar
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Limpiar();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Limpiar();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    // Antes de guardar: después del guardado EF ya no sabe qué entidades cambiaron
    private void AnotarCambios(DbContext? contexto)
    {
        if (contexto is null)
            return;

        foreach (var entrada in contexto.ChangeTracker.Entries())
        {
            if (entrada.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            switch (entrada.Entity)
            {
                // Se guarda la entidad: si es nueva, el Id recién existe después de guardar
                case Ruta ruta:
                    rutasCambiadas.Add(ruta);
                    break;
                case Parada parada:
                    paradasCambiadas.Add(parada);
                    break;
                case Envio envio when entrada.State == EntityState.Modified:
                    enviosCambiados.Add(envio.Id);
                    break;
                case Operador operador:
                    operadoresCambiados.Add(operador.Id);
                    break;
                case ReglaOperativa regla:
                    operadoresCambiados.Add(regla.OperadorId);
                    break;
            }
        }
    }

    private async Task InvalidarAsync(DbContext? contexto, CancellationToken cancellationToken)
    {
        try
        {
            if (contexto is ApplicationDbContext db)
            {
                foreach (var operadorId in operadoresCambiados)
                {
                    await cache.InvalidarAsync(CasosDeCache.Operador(operadorId), cancellationToken);
                }

                foreach (var (operadorId, rutaId) in await RutasAfectadasAsync(db, cancellationToken))
                {
                    await cache.InvalidarAsync(CasosDeCache.HojaDeRuta(operadorId, rutaId), cancellationToken);
                }
            }
        }
        finally
        {
            Limpiar();
        }
    }

    // Rutas cuya hoja quedó vieja, con su operador (la clave del caché lo lleva)
    private async Task<List<(int OperadorId, int RutaId)>> RutasAfectadasAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var afectadas = rutasCambiadas
            .Select(r => (r.OperadorId, r.Id))
            .ToHashSet();

        // Rutas de las paradas cambiadas y de los envíos cuyo estado cambió (puede que no esté cargada la ruta)
        var rutaIds = paradasCambiadas.Select(p => p.RutaId).ToHashSet();

        if (enviosCambiados.Count > 0)
        {
            var rutasDeEnvios = await db.Paradas
                .AsNoTracking()
                .Where(p => enviosCambiados.Contains(p.EnvioId))
                .Select(p => p.RutaId)
                .ToListAsync(cancellationToken);

            rutaIds.UnionWith(rutasDeEnvios);
        }

        rutaIds.ExceptWith(afectadas.Select(a => a.Id));

        if (rutaIds.Count > 0)
        {
            var rutas = await db.Rutas
                .AsNoTracking()
                .Where(r => rutaIds.Contains(r.Id))
                .Select(r => new { r.OperadorId, r.Id })
                .ToListAsync(cancellationToken);

            afectadas.UnionWith(rutas.Select(r => (r.OperadorId, r.Id)));
        }

        return afectadas.ToList();
    }

    private void Limpiar()
    {
        rutasCambiadas.Clear();
        paradasCambiadas.Clear();
        enviosCambiados.Clear();
        operadoresCambiados.Clear();
    }
}
