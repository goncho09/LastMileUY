using System.Net.Http.Json;
using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real. Si la sesión venció, ReenviarCookieApi lanza SesionVencidaException.
public class CacheApiHttp(HttpClient http) : ICacheApi
{
    public async Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<MetricaCacheDto>>("api/cache/metricas", ct) ?? [];
}
