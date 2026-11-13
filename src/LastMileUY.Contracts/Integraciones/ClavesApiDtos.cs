namespace LastMileUY.Contracts.Integraciones;

public static class AmbientesApi
{
    public const string Produccion = "Produccion";

    public const string Pruebas = "Pruebas";
}

public record ClaveApiDto(
    int Id,
    string Nombre,
    string Prefijo,
    DateTime FechaCreacion,
    DateTime? UltimoUso,
    DateTime? FechaRevocacion,
    string Ambiente = AmbientesApi.Produccion)
{
    public bool Activa => FechaRevocacion is null;

    public bool EsPrueba => Ambiente == AmbientesApi.Pruebas;
}

public class CrearClaveApiRequest
{
    public string Nombre { get; set; } = string.Empty;

    public string Ambiente { get; set; } = AmbientesApi.Produccion;
}

public record ClaveApiCreadaDto(ClaveApiDto Datos, string Clave);

public class SimularEstadoRequest
{
    public string Estado { get; set; } = string.Empty;
}
