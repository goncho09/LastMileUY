using System.Net;
using LastMileUY.Contracts.Seguimiento;

namespace LastMileUY.PublicTracking.Servicios;

public class SeguimientoApi(HttpClient http)
{
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
