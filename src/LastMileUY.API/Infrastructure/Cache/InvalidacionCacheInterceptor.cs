using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Cache;

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

    private void AnotarCambios(DbContext? contexto)
    {
        if (contexto is null)
            return;

        foreach (var entrada in contexto.ChangeTracker.Entries().ToList())
        {
            if (entrada.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            switch (entrada.Entity)
            {
                case Envio envio when entrada.State == EntityState.Modified:
                    codigosCambiados.Add(envio.CodigoSeguimiento);
                    break;
                case Bulto bulto when entrada.State == EntityState.Modified:
                    if (CodigoDelEnvio(contexto, bulto.EnvioId) is { } codigo)
                        codigosCambiados.Add(codigo);
                    break;
                case Operador operador:
                    operadoresCambiados.Add(operador.Id);
                    break;
                case ReglaOperativa regla:
                    operadoresCambiados.Add(regla.OperadorId);
                    break;
                case FuncionalidadOperador funcionalidad:
                    operadoresCambiados.Add(funcionalidad.OperadorId);
                    break;
            }
        }
    }

    private static string? CodigoDelEnvio(DbContext contexto, int envioId) =>
        contexto.Set<Envio>().Local.FirstOrDefault(e => e.Id == envioId)?.CodigoSeguimiento
        ?? contexto.Set<Envio>().AsNoTracking()
            .Where(e => e.Id == envioId)
            .Select(e => e.CodigoSeguimiento)
            .FirstOrDefault();

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
