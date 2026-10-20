using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace LastMileUY.DriverPWA.Services;

public class RutasService(HttpClient http)
{
    public async Task<List<RutaResumenDto>> ObtenerMisRutasAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://localhost:8443/api/rutas/mias"
        );

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include
        );

        using var response = await http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Tu sesión expiró.");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<RutaResumenDto>>()
               ?? [];
    }
    
    public async Task<RutaDetalleDto?> ObtenerRutaAsync(int id)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://localhost:8443/api/rutas/mias/{id}"
        );

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include
        );

        using var response = await http.SendAsync(request);

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
    

    public async Task ConfirmarEntregaAsync(int envioId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://localhost:8443/api/entregas/{envioId}/confirmar"
        );

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include
        );

        using var response = await http.SendAsync(request);
        
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Tu sesión expiró.");

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException(
                "No tenés permisos para confirmar esta entrega.");

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorEntrega>();

            throw new InvalidOperationException(
                error?.Mensaje ?? "No se pudo confirmar la entrega.");
        }

        response.EnsureSuccessStatusCode();
    }

    private sealed record ErrorEntrega(string Mensaje);

}
