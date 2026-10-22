using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Seguimiento;

namespace LastMileUY.PublicTracking.Servicios;

// Consulta de un envío por su código (GET /api/seguimiento/{codigo}, público: sin sesión ni cookies)
public class SeguimientoApi(HttpClient http)
{
    // null si no hay ningún envío con ese código.
    // Lanza ConsultasExcedidasException si la API frenó por exceso de consultas (429)
    // y HttpRequestException si la API no responde.
    public async Task<SeguimientoPublicoDto?> ObtenerAsync(string codigo, CancellationToken cancellationToken = default)
    {
        using var respuesta = await http.GetAsync(
            $"api/seguimiento/{Uri.EscapeDataString(codigo)}", cancellationToken);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ConsultasExcedidasException();

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<SeguimientoPublicoDto>(cancellationToken);
    }
}

public class ConsultasExcedidasException() : Exception("Se hicieron demasiadas consultas seguidas.");
