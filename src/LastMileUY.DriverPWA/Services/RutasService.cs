using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.DriverPWA.Services;

public class RutasService(HttpClient http)
{
    public async Task<List<RutaResumenDto>> ObtenerMisRutasAsync()
    {
        using var response = await http.GetAsync("api/rutas/mias");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Tu sesión expiró.");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<RutaResumenDto>>()
               ?? [];
    }

    public async Task<RutaDetalleDto?> ObtenerRutaAsync(int id)
    {
        using var response = await http.GetAsync($"api/rutas/mias/{id}");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Tu sesión expiró.");

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException(
                "No tenés permisos para consultar esta ruta."
            );

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RutaDetalleDto>();
    }
}
