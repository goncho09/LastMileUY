using LastMileUY.Contracts.Cache;

namespace LastMileUY.Backoffice.Servicios;

// Números fijos para trabajar el tablero sin la API (Api:UsarDatosDePrueba = true)
public class CacheApiDePrueba : ICacheApi
{
    public Task<List<MetricaCacheDto>> ObtenerMetricasAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<MetricaCacheDto>
        {
            new("operador", "Configuración del operador", "Se lee seguido y casi nunca cambia", 3600, 950, 50, 95.0),
            new("seguimiento", "Seguimiento público", "Mucho tráfico y el estado cambia pocas veces", 120, 820, 180, 82.0)
        });

    public Task ReiniciarMetricasAsync(CancellationToken ct = default) => Task.CompletedTask;
}
