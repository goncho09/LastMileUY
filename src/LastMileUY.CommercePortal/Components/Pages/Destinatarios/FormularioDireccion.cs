using System.Globalization;
using LastMileUY.Contracts.Destinatarios;

namespace LastMileUY.CommercePortal.Components.Pages.Destinatarios;

// Lo que se escribe en el formulario de una dirección. La ubicación va en un solo campo,
// "-34.9011, -56.1645", que es como la copia Google Maps (clic derecho sobre el punto)
public class FormularioDireccion
{
    public string Calle { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Apartamento { get; set; }
    public string Ciudad { get; set; } = string.Empty;
    public string Departamento { get; set; } = "Montevideo";
    public string? CodigoPostal { get; set; }
    public string? Referencia { get; set; }
    public string? Ubicacion { get; set; }

    // Los mismos controles que la API, para avisar antes de mandar; la API los vuelve a hacer
    public void Validar(List<string> errores)
    {
        if (string.IsNullOrWhiteSpace(Calle))
            errores.Add("Completá la calle.");

        if (string.IsNullOrWhiteSpace(Numero))
            errores.Add("Completá el número de puerta.");

        if (string.IsNullOrWhiteSpace(Ciudad))
            errores.Add("Completá la ciudad o localidad.");

        if (!DepartamentosUruguay.Todos.Contains(Departamento))
            errores.Add("Elegí el departamento.");

        if (!string.IsNullOrWhiteSpace(Ubicacion) && LeerUbicacion(Ubicacion) is null)
            errores.Add("La ubicación va como latitud y longitud separadas por coma, por ejemplo -34.9011, -56.1645.");
    }

    public CrearDireccionRequest ARequest()
    {
        var ubicacion = string.IsNullOrWhiteSpace(Ubicacion) ? null : LeerUbicacion(Ubicacion);

        return new CrearDireccionRequest
        {
            Calle = Calle.Trim(),
            Numero = Numero.Trim(),
            Apartamento = Limpio(Apartamento),
            Ciudad = Ciudad.Trim(),
            Departamento = Departamento,
            CodigoPostal = Limpio(CodigoPostal),
            Referencia = Limpio(Referencia),
            Latitud = ubicacion?.Latitud,
            Longitud = ubicacion?.Longitud
        };
    }

    // "lat, lon" con punto decimal; también acepta "lat lon" o "lat;lon". Fuera del mapa: null
    public static (decimal Latitud, decimal Longitud)? LeerUbicacion(string texto)
    {
        var partes = texto.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length != 2
            || !decimal.TryParse(partes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitud)
            || !decimal.TryParse(partes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitud)
            || latitud is < -90 or > 90
            || longitud is < -180 or > 180)
        {
            return null;
        }

        return (latitud, longitud);
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
