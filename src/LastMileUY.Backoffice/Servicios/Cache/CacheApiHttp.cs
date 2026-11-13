using System.Net;
using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

public class CacheApiHttp(HttpClient http) : ICacheApi
{
    public async Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync("api/cache/metricas", ct);

        if (respuesta.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new RedisNoDisponibleException();

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<List<MetricaCacheDto>>(ct) ?? [];
    }
}

public class RedisNoDisponibleException() : Exception("Redis no está disponible: no se pueden leer las métricas del caché.");
