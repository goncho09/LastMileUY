using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;

namespace LastMileUY.DriverPWA.Services;

public class AuthService(HttpClient http)
{
    public async Task<bool> LoginAsync(string email, string password)
    {
        using var response = await http.PostAsJsonAsync("api/auth/login", new
        {
            email,
            password,
            operadorId = (int?)null
        });

        return response.IsSuccessStatusCode;
    }

    public async Task LogoutAsync()
    {
        using var response = await http.PostAsync("api/auth/logout", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UsuarioAutenticadoResponse?> ObtenerUsuarioAsync()
    {
        using var response = await http.GetAsync("api/auth/me");

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
            .ReadFromJsonAsync<UsuarioAutenticadoResponse>();
    }
}
