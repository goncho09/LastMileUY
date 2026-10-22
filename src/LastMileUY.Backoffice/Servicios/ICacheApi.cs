using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

// Métricas del caché de la API para el tablero técnico (GET /api/cache/metricas)
public interface ICacheApi
{
    Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default);

    Task ReiniciarMetricasAsync(CancellationToken ct = default);
}
