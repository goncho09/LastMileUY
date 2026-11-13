using System.Collections.Concurrent;
using StackExchange.Redis;

namespace LastMileUY.API.Infrastructure.Cache;

public interface IMetricasCache
{
    void Registrar(CasoDeCache caso, bool acierto);

    Task<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>> ObtenerAsync(CancellationToken cancellationToken = default);

    Task ReiniciarAsync(CancellationToken cancellationToken = default);
}

public class MetricasCacheRedis(IConnectionMultiplexer redis, ILogger<MetricasCacheRedis> logger) : IMetricasCache
{
    public const string Clave = CasosDeCache.PrefijoRedis + "metricas:cache";

    public void Registrar(CasoDeCache caso, bool acierto)
    {
        try
        {
            redis.GetDatabase().HashIncrement(Clave, Campo(caso.Id, acierto), 1, CommandFlags.FireAndForget);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo registrar la métrica del caché {Caso}", caso.Id);
        }
    }

    public async Task<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>> ObtenerAsync(
        CancellationToken cancellationToken = default)
    {
        HashEntry[] guardados;

        try
        {
            guardados = await redis.GetDatabase().HashGetAllAsync(Clave);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new MetricasCacheNoDisponiblesException(ex);
        }

        var campos = guardados.ToDictionary(c => c.Name.ToString(), c => (long)c.Value);

        return CasosDeCache.Todos.ToDictionary(
            caso => caso.Id,
            caso => (campos.GetValueOrDefault(Campo(caso.Id, true)), campos.GetValueOrDefault(Campo(caso.Id, false))));
    }

    public async Task ReiniciarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Clave);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new MetricasCacheNoDisponiblesException(ex);
        }
    }

    private static string Campo(string casoId, bool acierto) => $"{casoId}:{(acierto ? "aciertos" : "fallos")}";
}

public class MetricasCacheNoDisponiblesException(Exception inner)
    : Exception("Redis no está disponible: no se pueden leer las métricas del caché.", inner);

public class MetricasCacheMemoria : IMetricasCache
{
    private readonly ConcurrentDictionary<string, long> aciertos = new();
    private readonly ConcurrentDictionary<string, long> fallos = new();

    public void Registrar(CasoDeCache caso, bool acierto) =>
        (acierto ? aciertos : fallos).AddOrUpdate(caso.Id, 1, (_, actual) => actual + 1);

    public Task<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>> ObtenerAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>>(
            CasosDeCache.Todos.ToDictionary(
                caso => caso.Id,
                caso => (aciertos.GetValueOrDefault(caso.Id), fallos.GetValueOrDefault(caso.Id))));

    public Task ReiniciarAsync(CancellationToken cancellationToken = default)
    {
        aciertos.Clear();
        fallos.Clear();
        return Task.CompletedTask;
    }
}
