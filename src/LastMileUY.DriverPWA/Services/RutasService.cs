
using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.DriverPWA.Services;

public class RutasService(HttpClient http, AlmacenLocal almacen)
{
    private static readonly TimeSpan DuracionCache = TimeSpan.FromHours(24);

    private const string ClaveMisRutas = "driver:mis-rutas";

    private static string ClaveRuta(int id) => $"driver:ruta:{id}";

    public async Task<List<RutaResumenDto>> ObtenerMisRutasAsync()
    {
        try
        {
            using var response = await http.GetAsync("api/rutas/mias");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Tu sesión expiró.");

            response.EnsureSuccessStatusCode();

            var rutas = await response.Content
                .ReadFromJsonAsync<List<RutaResumenDto>>() ?? [];

            await almacen.GuardarAsync(ClaveMisRutas, rutas, DuracionCache);

            return rutas;
        }
        catch (HttpRequestException)
        {
            return await ObtenerMisRutasGuardadasAsync();
        }
        catch (TaskCanceledException)
        {
            return await ObtenerMisRutasGuardadasAsync();
        }
    }

    public async Task<RutaDetalleDto?> ObtenerRutaAsync(int id)
    {
        try
        {
            using var response = await http.GetAsync($"api/rutas/mias/{id}");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Tu sesión expiró.");

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException(
                    "No tenés permisos para consultar esta ruta.");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var ruta = await response.Content
                .ReadFromJsonAsync<RutaDetalleDto>();

            if (ruta is not null)
                await almacen.GuardarAsync(ClaveRuta(id), ruta, DuracionCache);

            return ruta;
        }
        catch (HttpRequestException)
        {
            return await ObtenerRutaGuardadaAsync(id);
        }
        catch (TaskCanceledException)
        {
            return await ObtenerRutaGuardadaAsync(id);
        }
    }

    private async Task<List<RutaResumenDto>> ObtenerMisRutasGuardadasAsync()
    {
        return await almacen.LeerAsync<List<RutaResumenDto>>(
            ClaveMisRutas) ?? [];
    }

    private async Task<RutaDetalleDto?> ObtenerRutaGuardadaAsync(int id)
    {
        return await almacen.LeerAsync<RutaDetalleDto>(
            ClaveRuta(id));
    }
}
