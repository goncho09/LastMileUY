using System.Diagnostics.Metrics;

namespace LastMileUY.API.Infrastructure.Cache;

// Las mismas lecturas del caché que cuenta IMetricasCache, como métrica de OpenTelemetry: así la relación
// de aciertos también se ve en el tablero técnico de Grafana (letra 6.12), por caso.
// Prometheus la guarda como lastmileuy_cache_lecturas_total{caso, resultado="acierto"|"fallo"}.
// A diferencia de los contadores en Redis, sigue contando aunque Redis esté caído.
public static class TelemetriaCache
{
    public const string Nombre = "LastMileUY.Cache";

    private static readonly Meter Medidor = new(Nombre);

    private static readonly Counter<long> Lecturas = Medidor.CreateCounter<long>(
        "lastmileuy.cache.lecturas",
        description: "Lecturas del caché distribuido: aciertos (estaba en el caché) y fallos (se fue a la base)");

    // Crea las series en 0 al arrancar. Prometheus calcula los aumentos comparando dos muestras: si una
    // serie aparece recién cuando pasa la primera vez (ya con 1), ese primer evento no se ve en el tablero.
    public static void Inicializar()
    {
        foreach (var caso in CasosDeCache.Todos)
        {
            Lecturas.Add(0, new KeyValuePair<string, object?>("caso", caso.Id), new KeyValuePair<string, object?>("resultado", "acierto"));
            Lecturas.Add(0, new KeyValuePair<string, object?>("caso", caso.Id), new KeyValuePair<string, object?>("resultado", "fallo"));
        }
    }

    public static void Registrar(CasoDeCache caso, bool acierto) =>
        Lecturas.Add(1,
            new KeyValuePair<string, object?>("caso", caso.Id),
            new KeyValuePair<string, object?>("resultado", acierto ? "acierto" : "fallo"));
}
