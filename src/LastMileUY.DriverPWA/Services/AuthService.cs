using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace LastMileUY.DriverPWA.Services;

public class AuthService(HttpClient http)
{
    public async Task<bool> LoginAsync(string email, string password)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://localhost:8443/api/auth/login"
        );

        request.Content = JsonContent.Create(new
        {
            email,
            password,
            operadorId = (int?)null
        });

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include
        );

        using var response = await http.SendAsync(request);

        return response.IsSuccessStatusCode;
    }
    
    
    public async Task LogoutAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://localhost:8443/api/auth/logout"
        );

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include
        );

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    
    public async Task<UsuarioAutenticadoResponse?> ObtenerUsuarioAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://localhost:8443/api/auth/me");

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include);

        using var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
            .ReadFromJsonAsync<UsuarioAutenticadoResponse>();
    }

}