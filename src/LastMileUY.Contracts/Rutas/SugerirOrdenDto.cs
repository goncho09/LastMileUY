namespace LastMileUY.Contracts.Rutas;

// POST /api/rutas/sugerir-orden: los envíos elegidos para una ruta, en el orden en que están ahora
public class SugerirOrdenRequest
{
    public List<int> EnvioIds { get; set; } = [];
}

// El orden sugerido (OrdenDeParadas) y cuántos kilómetros en línea recta tiene el recorrido antes y
// después. SinUbicacion: los códigos de los envíos cuya dirección no tiene ubicación (van al final).
// DesdeDeposito: si los kilómetros se cuentan desde el depósito del operador (false si no lo cargó).
public record SugerenciaOrdenDto(
    IReadOnlyList<int> EnvioIds,
    double KmActual,
    double KmSugerido,
    IReadOnlyList<string> SinUbicacion,
    bool DesdeDeposito);
