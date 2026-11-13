using System.Diagnostics.Metrics;

namespace LastMileUY.Worker;

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
