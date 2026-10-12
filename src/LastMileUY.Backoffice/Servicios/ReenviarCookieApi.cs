using System.Net;

namespace LastMileUY.Backoffice.Servicios;

// Agrega a cada llamada a la API la cookie de sesión del usuario logueado en el Backoffice.
// Si la API responde 401 (la sesión venció), avisa con SesionVencidaException para que se vuelva al login.
public class ReenviarCookieApi(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    // Claim donde se guarda la cookie de la API, dentro de la cookie (cifrada) del Backoffice
    public const string ClaimCookieApi = "cookie_api";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var cookieApi = httpContextAccessor.HttpContext?.User.FindFirst(ClaimCookieApi)?.Value;

        if (!string.IsNullOrEmpty(cookieApi))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", cookieApi);
        }

        var respuesta = await base.SendAsync(request, ct);

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            respuesta.Dispose();
            throw new SesionVencidaException();
        }

        return respuesta;
    }
}

public class SesionVencidaException() : Exception("La sesión con la API venció o no es válida.");
