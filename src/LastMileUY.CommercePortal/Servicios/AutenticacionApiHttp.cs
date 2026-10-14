using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;

namespace LastMileUY.CommercePortal.Servicios;

// POST api/auth/login (con el operador elegido, si hace falta) y después GET api/auth/me.
// La API responde con su cookie de sesión (Set-Cookie): se guarda para reenviarla en cada llamada.
public class AutenticacionApiHttp(HttpClient http) : IAutenticacionApi
{
    public async Task<ResultadoLogin> IniciarSesionAsync(
        string email, string password, int? operadorId, CancellationToken ct = default)
    {
        using var login = await http.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest { Email = email, Password = password, OperadorId = operadorId },
            ct);

        switch (login.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return new ResultadoLogin(EstadoLogin.CredencialesInvalidas);

            // El comercio trabaja con varios operadores: hay que elegir uno
            case HttpStatusCode.Conflict:
                var requerido = await login.Content.ReadFromJsonAsync<OperadorRequeridoResponse>(ct);
                return new ResultadoLogin(
                    EstadoLogin.ElegirOperador,
                    Mensaje: requerido?.Mensaje,
                    Operadores: requerido?.Operadores ?? []);

            case HttpStatusCode.BadRequest or HttpStatusCode.Forbidden:
                return new ResultadoLogin(EstadoLogin.Rechazado, Mensaje: await LeerMensajeAsync(login, ct));
        }

        login.EnsureSuccessStatusCode();

        return await ObtenerSesionAsync(LeerCookieObligatoria(login), ct);
    }

    public async Task<List<OperadorDisponibleDto>> ListarOperadoresAsync(string cookieApi, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, "api/auth/operadores");
        pedido.Headers.Add("Cookie", cookieApi);

        using var respuesta = await http.SendAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            throw new SesionVencidaException();

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<List<OperadorDisponibleDto>>(ct) ?? [];
    }

    public async Task<ResultadoLogin> CambiarOperadorAsync(string cookieApi, int operadorId, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "api/auth/operador")
        {
            Content = JsonContent.Create(new CambiarOperadorRequest { OperadorId = operadorId })
        };
        pedido.Headers.Add("Cookie", cookieApi);

        using var respuesta = await http.SendAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            throw new SesionVencidaException();

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            return new ResultadoLogin(EstadoLogin.Rechazado, Mensaje: await LeerMensajeAsync(respuesta, ct));

        respuesta.EnsureSuccessStatusCode();

        // La API renovó la sesión con el nuevo operador: viene una cookie nueva
        return await ObtenerSesionAsync(LeerCookieObligatoria(respuesta), ct);
    }

    public async Task CerrarSesionAsync(string cookieApi, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
        pedido.Headers.Add("Cookie", cookieApi);

        using var _ = await http.SendAsync(pedido, ct);
    }

    // Con la cookie nueva pregunta quién es y con qué comercio/operador quedó la sesión
    private async Task<ResultadoLogin> ObtenerSesionAsync(string cookieApi, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        pedido.Headers.Add("Cookie", cookieApi);

        using var respuesta = await http.SendAsync(pedido, ct);
        respuesta.EnsureSuccessStatusCode();

        var usuario = await respuesta.Content.ReadFromJsonAsync<UsuarioAutenticadoResponse>(ct)
            ?? throw new InvalidOperationException("La API no devolvió los datos del usuario.");

        // El portal es solo para usuarios de comercio
        if (usuario.ComercioId is not int comercioId || usuario.OperadorId is not int operador)
        {
            return new ResultadoLogin(
                EstadoLogin.Rechazado,
                Mensaje: "Este usuario no es de un comercio. El portal es para comercios clientes.");
        }

        return new ResultadoLogin(
            EstadoLogin.Ok,
            new SesionApi(
                $"{usuario.Nombre} {usuario.Apellido}".Trim(),
                usuario.Email,
                comercioId,
                operador,
                usuario.OperadorNombre,
                cookieApi));
    }

    private static string LeerCookieObligatoria(HttpResponseMessage respuesta) =>
        LeerCookies(respuesta)
        ?? throw new InvalidOperationException("La API no devolvió la cookie de sesión.");

    // Arma el encabezado Cookie con todo lo que vino en Set-Cookie ("nombre=valor; nombre2=valor2")
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

    internal static async Task<string> LeerMensajeAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<MensajeApi>(ct);

            if (!string.IsNullOrWhiteSpace(cuerpo?.Mensaje))
                return cuerpo.Mensaje;
        }
        catch (System.Text.Json.JsonException)
        {
            // El cuerpo no era el JSON esperado: se usa el mensaje genérico
        }

        return "La operación fue rechazada por el servidor.";
    }

    private record MensajeApi(string? Mensaje);
}
