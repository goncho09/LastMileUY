using System.Diagnostics.Metrics;
using LastMileUY.API.Modules.Sincronizacion.Domain;

namespace LastMileUY.API.Modules.Sincronizacion.Application;

public static class TelemetriaSincronizacion
{
    public const string Nombre = "LastMileUY.Sincronizacion";
    public const string NombreDemora = "lastmileuy.sincronizacion.demora";

    private static readonly Meter Medidor = new(Nombre);

    private static readonly Counter<long> Operaciones = Medidor.CreateCounter<long>(
        "lastmileuy.sincronizacion.operaciones",
        description: "Operaciones de la app del repartidor que llegaron a la API, por resultado");

    private static readonly Histogram<double> Demora = Medidor.CreateHistogram<double>(
        NombreDemora,
        unit: "s",
        description: "Cuánto estuvo pendiente cada operación en el celular antes de sincronizarse");

    public static void Inicializar()
    {
        foreach (var resultado in Enum.GetValues<ResultadoOperacion>())
            Operaciones.Add(0, new KeyValuePair<string, object?>("resultado", resultado.ToString()));

        Demora.Record(0);
    }

    public static void Registrar(ResultadoOperacion resultado, DateTime registradaEnDispositivo)
    {
        Operaciones.Add(1, new KeyValuePair<string, object?>("resultado", resultado.ToString()));

        var demora = Math.Max(0, (DateTime.UtcNow - registradaEnDispositivo.ToUniversalTime()).TotalSeconds);
        Demora.Record(demora);
    }
}
