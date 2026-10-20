using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace LastMileUY.API.Infrastructure.Cache;

// Caché común para cualquier consulta (ver ADR de caché): "si está en el caché lo devuelvo;
// si no, lo consulto en la base y lo guardo por el tiempo indicado".
// Para cachear un caso nuevo alcanza con envolver su consulta con ObtenerOCrearAsync.
// Si Redis no responde se consulta la base igual: el caché nunca deja la API sin servicio.
public class CacheDistribuido(IDistributedCache cache, ILogger<CacheDistribuido> logger)
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    public async Task<T?> ObtenerOCrearAsync<T>(
        string clave,
        Func<CancellationToken, Task<T?>> consultar,
        TimeSpan duracion,
        CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var guardado = await cache.GetAsync(clave, cancellationToken);

            if (guardado is not null)
            {
                return JsonSerializer.Deserialize<T>(guardado, OpcionesJson);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo leer {Clave} del caché; se consulta la base", clave);
        }

        var valor = await consultar(cancellationToken);

        // No se guarda "no existe": así un dato que se crea después aparece enseguida
        if (valor is not null)
        {
            try
            {
                await cache.SetAsync(
                    clave,
                    JsonSerializer.SerializeToUtf8Bytes(valor, OpcionesJson),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duracion },
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
