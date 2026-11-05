using System.Diagnostics.Metrics;
using LastMileUY.API.Modules.Sincronizacion.Domain;

namespace LastMileUY.API.Modules.Sincronizacion.Application;

// Métricas de OpenTelemetry de la sincronización de la app del repartidor, para el tablero técnico de
// Grafana (letra 6.12, "sincronizaciones pendientes"). Lo que todavía está en un celular sin conexión el
// servidor no lo puede ver; lo que sí ve es cada operación cuando llega:
// - lastmileuy_sincronizacion_operaciones_total{resultado="Aplicada"|"Rechazada"|"Conflicto"}
// - lastmileuy_sincronizacion_demora_seconds: cuánto estuvo pendiente en el celular (de que el repartidor
//   la registró a que llegó a la API). Sin conexión, la demora crece.
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

    // Crea las series en 0 al arrancar. Prometheus calcula los aumentos comparando dos muestras: si una
    // serie aparece recién cuando pasa la primera vez (ya con 1), ese primer evento no se ve en el tablero.
    // La demora arranca con una medición en 0 por el mismo motivo.
    public static void Inicializar()
    {
        foreach (var resultado in Enum.GetValues<ResultadoOperacion>())
            Operaciones.Add(0, new KeyValuePair<string, object?>("resultado", resultado.ToString()));

        Demora.Record(0);
    }

    public static void Registrar(ResultadoOperacion resultado, DateTime registradaEnDispositivo)
    {
        Operaciones.Add(1, new KeyValuePair<string, object?>("resultado", resultado.ToString()));

        // Si el reloj del celular está adelantado, la demora daría negativa
        var demora = Math.Max(0, (DateTime.UtcNow - registradaEnDispositivo.ToUniversalTime()).TotalSeconds);
        Demora.Record(demora);
    }
}
