using System.Diagnostics.Metrics;

namespace LastMileUY.Worker;

// Métricas de OpenTelemetry de los avisos a comercios, para el tablero técnico de Grafana (letra 6.12):
// - lastmileuy_avisos_intentos_total{resultado="entregado"|"fallido"}: cada intento de mandar un aviso.
// - lastmileuy_avisos_fallidos_total: avisos que agotaron sus intentos y quedaron Fallidos (los que el
//   comercio ve como "No recibido" y puede reenviar).
public static class TelemetriaAvisos
{
    public const string Nombre = "LastMileUY.Avisos";

    private static readonly Meter Medidor = new(Nombre);

    private static readonly Counter<long> Intentos = Medidor.CreateCounter<long>(
        "lastmileuy.avisos.intentos",
        description: "Intentos de mandar un aviso al sistema de un comercio");

    private static readonly Counter<long> Fallidos = Medidor.CreateCounter<long>(
        "lastmileuy.avisos.fallidos",
        description: "Avisos que agotaron sus intentos y quedaron fallidos");

    // Crea las series en 0 al arrancar. Prometheus calcula los aumentos comparando dos muestras: si una
    // serie aparece recién cuando pasa la primera vez (ya con 1), ese primer evento no se ve en el tablero.
    public static void Inicializar()
    {
        Intentos.Add(0, new KeyValuePair<string, object?>("resultado", "entregado"));
        Intentos.Add(0, new KeyValuePair<string, object?>("resultado", "fallido"));
        Fallidos.Add(0);
    }

    public static void RegistrarIntento(bool entregado, bool quedoFallido)
    {
        Intentos.Add(1, new KeyValuePair<string, object?>("resultado", entregado ? "entregado" : "fallido"));

        if (quedoFallido)
            Fallidos.Add(1);
    }
}
