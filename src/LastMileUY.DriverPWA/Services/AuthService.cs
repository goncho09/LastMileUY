using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;

namespace LastMileUY.DriverPWA.Services;

public class AuthService(HttpClient http, AlmacenLocal almacen, ColaOperacionesService cola)
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

        var usuario = await ObtenerUsuarioAsync();

        if (usuario is not null)
            await almacen.AsignarDuenoAsync(usuario.Id);

        return true;
    }

    public async Task LogoutAsync()
    {
        var pendientes = await cola.ObtenerPendientesAsync();

        if (pendientes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Tenés {pendientes.Count} operaciones pendientes de sincronización. " +
                "Sincronizalas antes de cerrar sesión para no perderlas.");
        }

        using var response = await http.PostAsync("api/auth/logout", null);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            response.EnsureSuccessStatusCode();

        await almacen.BorrarTodoAsync();
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
