using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;

namespace LastMileUY.DriverPWA.Services;

public class AuthService(HttpClient http, AlmacenLocal almacen)
{
    public async Task<bool> LoginAsync(string email, string password)
    {
        using var response = await http.PostAsJsonAsync("api/auth/login", new
        {
            email,
            password,
            operadorId = (int?)null
        });

        if (!response.IsSuccessStatusCode)
            return false;

        // Lo que haya quedado guardado en el celular de otro repartidor se borra antes de seguir
        var usuario = await ObtenerUsuarioAsync();

        if (usuario is not null)
            await almacen.AsignarDuenoAsync(usuario.Id);

        return true;
    }

    public async Task LogoutAsync()
    {
        // Primero lo local: aunque la API no responda, en el celular no quedan datos personales
        await almacen.BorrarTodoAsync();

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
