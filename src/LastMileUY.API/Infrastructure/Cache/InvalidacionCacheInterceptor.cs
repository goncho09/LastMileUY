using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Cache;

// Invalidación del caché (ver ADR de caché): cada vez que se guarda algo en la base, mira qué cambió
// y borra del caché lo que quedó viejo. Al estar en un solo lugar, cualquier código que cambie
// envíos o la configuración del operador invalida solo, aunque no sepa que existe el caché.
//   - Seguimiento público: cambia el envío (su estado y su historial): sincronización del repartidor,
//     cambio de estado en el Backoffice, armado y despacho de rutas.
//   - Configuración del operador: cambia el operador (nombre, color, logo) o una regla operativa.
// Se crea uno por pedido (con su DbContext), por eso puede guardar lo pendiente en campos.
public class InvalidacionCacheInterceptor(CacheDistribuido cache) : SaveChangesInterceptor
{
    private readonly HashSet<string> codigosCambiados = [];
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
        await InvalidarAsync(cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        InvalidarAsync(CancellationToken.None).GetAwaiter().GetResult();
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
                case Envio envio when entrada.State == EntityState.Modified:
                    codigosCambiados.Add(envio.CodigoSeguimiento);
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

    private async Task InvalidarAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var codigo in codigosCambiados)
            {
                await cache.InvalidarAsync(CasosDeCache.ClaveSeguimiento(codigo), cancellationToken);
            }

            foreach (var operadorId in operadoresCambiados)
            {
                await cache.InvalidarAsync(CasosDeCache.ClaveOperador(operadorId), cancellationToken);
            }
        }
        finally
        {
            Limpiar();
        }
    }

    private void Limpiar()
    {
        codigosCambiados.Clear();
        operadoresCambiados.Clear();
    }
}
