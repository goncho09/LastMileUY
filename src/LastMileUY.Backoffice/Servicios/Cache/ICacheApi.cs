using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

public interface ICacheApi
{
    Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default);
}
