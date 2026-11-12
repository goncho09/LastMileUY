namespace LastMileUY.Contracts.Integraciones;

// Una clave de la API pública del comercio, sin el secreto: solo el comienzo, para reconocerla
// (GET /api/claves-api)
public record ClaveApiDto(
    int Id,
    string Nombre,
    string Prefijo,
    DateTime FechaCreacion,
    DateTime? UltimoUso,
    DateTime? FechaRevocacion)
{
    public bool Activa => FechaRevocacion is null;
}

// Alta de una clave (POST /api/claves-api). El nombre es para que el comercio sepa dónde la usa
public class CrearClaveApiRequest
{
    public string Nombre { get; set; } = string.Empty;
}

// Respuesta al crear una clave: la clave completa viaja solamente esta vez (la API guarda su hash)
public record ClaveApiCreadaDto(ClaveApiDto Datos, string Clave);
