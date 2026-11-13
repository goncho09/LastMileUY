using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace LastMileUY.API.Infrastructure.Cache;

public class CacheDistribuido(IDistributedCache cache, IMetricasCache metricas, ILogger<CacheDistribuido> logger)
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    public async Task<T?> ObtenerOCrearAsync<T>(
        CasoDeCache caso,
        string clave,
        Func<CancellationToken, Task<T?>> consultar,
        CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var guardado = await cache.GetAsync(clave, cancellationToken);

            if (guardado is not null)
            {
                var valorGuardado = JsonSerializer.Deserialize<T>(guardado, OpcionesJson);
                metricas.Registrar(caso, acierto: true);
                TelemetriaCache.Registrar(caso, acierto: true);
                return valorGuardado;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo leer {Clave} del caché; se consulta la base", clave);
        }

        metricas.Registrar(caso, acierto: false);
        TelemetriaCache.Registrar(caso, acierto: false);

        var valor = await consultar(cancellationToken);

        if (valor is not null)
        {
            try
            {
                await cache.SetAsync(
                    clave,
                    JsonSerializer.SerializeToUtf8Bytes(valor, OpcionesJson),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = caso.Duracion },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo guardar {Clave} en el caché", clave);
            }
        }

        return valor;
    }

    public async Task InvalidarAsync(string clave, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(clave, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo invalidar {Clave} en el caché", clave);
        }
    }
}
