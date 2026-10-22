using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace LastMileUY.API.Infrastructure.Cache;

// Caché común para cualquier consulta (ver ADR de caché): "si está en el caché lo devuelvo;
// si no, lo consulto en la base y lo guardo por el tiempo indicado".
// Para cachear un caso nuevo alcanza con agregarlo en CasosDeCache y envolver su consulta con ObtenerOCrearAsync.
// Si Redis no responde se consulta la base igual: el caché nunca deja la API sin servicio.
// Cada lectura cuenta como acierto (estaba en el caché) o fallo (hubo que ir a la base): ver IMetricasCache.
public class CacheDistribuido(IDistributedCache cache, IMetricasCache metricas, ILogger<CacheDistribuido> logger)
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    // El caso dice cuánto dura el dato y en qué contador se anota la lectura
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
                return valorGuardado;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo leer {Clave} del caché; se consulta la base", clave);
        }

        // No estaba en el caché (o el caché no respondió): se va a la base
        metricas.Registrar(caso, acierto: false);

        var valor = await consultar(cancellationToken);

        // No se guarda "no existe": así un dato que se crea después aparece enseguida
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

    // Borra un dato del caché porque cambió en la base (la próxima lectura va a la base)
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
