using System.Diagnostics.Metrics;

namespace LastMileUY.API.Infrastructure.Cache;

public static class TelemetriaCache
{
    public const string Nombre = "LastMileUY.Cache";

    private static readonly Meter Medidor = new(Nombre);

    private static readonly Counter<long> Lecturas = Medidor.CreateCounter<long>(
        "lastmileuy.cache.lecturas",
        description: "Lecturas del caché distribuido: aciertos (estaba en el caché) y fallos (se fue a la base)");

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
