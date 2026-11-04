using System.Diagnostics;

namespace LastMileUY.Contracts.Mensajeria;

public static class TelemetriaMensajeria
{
    public const string Nombre = "LastMileUY.Mensajeria";

    public static readonly ActivitySource Actividades = new(Nombre);
}