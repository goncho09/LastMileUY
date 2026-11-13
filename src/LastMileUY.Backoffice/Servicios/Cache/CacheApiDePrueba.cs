using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

public class CacheApiDePrueba : ICacheApi
{
    public Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<MetricaCacheDto>
        {
            new("operador", "Configuración del operador", "Se lee seguido y casi nunca cambia", 3600, 950, 50, 95.0),
            new("seguimiento", "Seguimiento público", "Mucho tráfico y el estado cambia pocas veces", 120, 820, 180, 82.0)
        });
}
