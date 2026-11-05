using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real. Si la sesión venció, ReenviarCookieApi lanza SesionVencidaException.
public class CacheApiHttp(HttpClient http) : ICacheApi
{
    public async Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync("api/cache/metricas", ct);

        // 503: la API anda, pero Redis (donde viven los contadores) no responde
        if (respuesta.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new RedisNoDisponibleException();

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<List<MetricaCacheDto>>(ct) ?? [];
    }
}

public class RedisNoDisponibleException() : Exception("Redis no está disponible: no se pueden leer las métricas del caché.");
