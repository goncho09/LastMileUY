using System.Net;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public class AutenticacionApiHttp(HttpClient http) : IAutenticacionApi
{
    public async Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        using var login = await http.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest { Email = email, Password = password },
            ct);

        if (login.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        if (login.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            return new SesionIniciada(email, email, OperadorId: null, CookieApi: null);
        }

        login.EnsureSuccessStatusCode();

        var cookieApi = LeerCookies(login)
            ?? throw new InvalidOperationException("La API no devolvió la cookie de sesión.");

        var usuario = await ObtenerAsync<UsuarioAutenticadoResponse>("api/auth/me", cookieApi, ct)
            ?? throw new InvalidOperationException("La API no devolvió los datos del usuario.");

        var operadorId = usuario.ComercioId is null ? usuario.OperadorId : null;

        var operador = operadorId is null
            ? null
            : await ObtenerAsync<OperadorActualDto>("api/operador", cookieApi, ct);

        return new SesionIniciada(
            $"{usuario.Nombre} {usuario.Apellido}".Trim(),
            usuario.Email,
            operadorId,
            cookieApi,
            operador,
            usuario.Roles.ToList());
    }

    private async Task<T?> ObtenerAsync<T>(string url, string cookieApi, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Add("Cookie", cookieApi);

        using var respuesta = await http.SendAsync(pedido, ct);
        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<T>(ct);
    }

    public static string? LeerCookies(HttpResponseMessage respuesta)
    {
        if (!respuesta.Headers.TryGetValues("Set-Cookie", out var encabezados))
        {
            return null;
        }

        var pares = encabezados
            .Select(e => e.Split(';', 2)[0].Trim())
            .Where(par => par.Contains('='))
            .ToList();

        return pares.Count == 0 ? null : string.Join("; ", pares);
    }
}
