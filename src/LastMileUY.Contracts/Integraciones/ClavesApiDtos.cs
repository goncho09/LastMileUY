namespace LastMileUY.Contracts.Integraciones;

// Los dos ambientes de la API pública (docs/api-publica.md)
public static class AmbientesApi
{
    // Envíos de verdad, con el operador logístico
    public const string Produccion = "Produccion";

    // Datos de prueba, aislados de los de verdad: para probar la integración sin crear envíos reales
    public const string Pruebas = "Pruebas";
}

// Una clave de la API pública del comercio, sin el secreto: solo el comienzo, para reconocerla
// (GET /api/claves-api)
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

// Alta de una clave (POST /api/claves-api). El nombre es para que el comercio sepa dónde la usa
public class CrearClaveApiRequest
{
    public string Nombre { get; set; } = string.Empty;

    // Produccion (por defecto) o Pruebas
    public string Ambiente { get; set; } = AmbientesApi.Produccion;
}

// Respuesta al crear una clave: la clave completa viaja solamente esta vez (la API guarda su hash)
public record ClaveApiCreadaDto(ClaveApiDto Datos, string Clave);

// Simulación en el ambiente de pruebas (POST /api/v1/pruebas/envios/{id}/simular): hasta qué estado
// avanza el envío de prueba (EnDeposito, AsignadoARuta, EnTransito, Entregado o NoEntregado)
public class SimularEstadoRequest
{
    public string Estado { get; set; } = string.Empty;
}
