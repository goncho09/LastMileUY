using StackExchange.Redis;

namespace LastMileUY.API.Infrastructure.Cache;

public sealed class LimpiezaCacheRedis(IConnectionMultiplexer redis, ILogger<LimpiezaCacheRedis> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionFailed += AlCaerse;
        redis.ConnectionRestored += AlReconectarse;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionFailed -= AlCaerse;
        redis.ConnectionRestored -= AlReconectarse;
        return Task.CompletedTask;
    }

    private void AlCaerse(object? sender, ConnectionFailedEventArgs e)
    {
        if (e.ConnectionType != ConnectionType.Interactive) return;

        logger.LogWarning("Se perdió la conexión con Redis ({Motivo}): el caché se saltea y se consulta la base", e.FailureType);
    }

    private void AlReconectarse(object? sender, ConnectionFailedEventArgs e)
    {
        if (e.ConnectionType != ConnectionType.Interactive) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var borrados = await LimpiarAsync(redis);
                logger.LogWarning(
                    "Se volvió a conectar a Redis: se borraron {Cantidad} datos del caché por si quedaron viejos",
                    borrados);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Se volvió a conectar a Redis pero no se pudo limpiar el caché");
            }
        });
    }

    public static async Task<int> LimpiarAsync(IConnectionMultiplexer redis)
    {
        var db = redis.GetDatabase();
        var borrados = 0;

        foreach (var servidor in redis.GetServers())
        {
            if (!servidor.IsConnected || servidor.IsReplica) continue;

            var claves = new List<RedisKey>();

            await foreach (var clave in servidor.KeysAsync(db.Database, pattern: CasosDeCache.PrefijoRedis + "*"))
            {
                if (clave != MetricasCacheRedis.Clave)
                    claves.Add(clave);
            }

            if (claves.Count > 0)
                borrados += (int)await db.KeyDeleteAsync([.. claves]);
        }

        return borrados;
    }
}
