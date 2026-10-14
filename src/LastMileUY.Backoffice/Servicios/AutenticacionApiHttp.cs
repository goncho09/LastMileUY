using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// Login contra la API real: POST api/auth/login, GET api/auth/me para saber quién es
// y GET api/operador para la identidad visual de su operador.
// La API responde con una cookie de sesión (Set-Cookie); se guarda para reenviarla en cada llamada.
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

        // Usuario de comercio al que la API le pide elegir operador (409) o que no puede entrar (400/403):
        // el Backoffice es solo del operador, así que se trata como "sin operador" y el login lo rechaza.
        if (login.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            return new SesionIniciada(email, email, OperadorId: null, CookieApi: null);
        }

        login.EnsureSuccessStatusCode();

        var cookieApi = LeerCookies(login)
            ?? throw new InvalidOperationException("La API no devolvió la cookie de sesión.");

        var usuario = await ObtenerAsync<UsuarioAutenticadoResponse>("api/auth/me", cookieApi, ct)
            ?? throw new InvalidOperationException("La API no devolvió los datos del usuario.");

        // Un usuario de comercio ahora también trae el operador que eligió al entrar,
        // pero no es personal del operador: no debe entrar al Backoffice.
        var operadorId = usuario.ComercioId is null ? usuario.OperadorId : null;

        // Nombre, color y logo del operador, para mostrarlos en todas las páginas
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

    // GET con la cookie de la sesión recién iniciada (todavía no está en la sesión del Backoffice)
    private async Task<T?> ObtenerAsync<T>(string url, string cookieApi, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Add("Cookie", cookieApi);

        using var respuesta = await http.SendAsync(pedido, ct);
        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<T>(ct);
    }

    // Arma el encabezado Cookie con todo lo que vino en Set-Cookie ("nombre=valor; nombre2=valor2").
    // Se toman todas porque ASP.NET parte la cookie en varias si es muy larga.
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
