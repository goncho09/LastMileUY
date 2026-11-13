using System.Net;

namespace LastMileUY.Backoffice.Servicios;

public class ReenviarCookieApi(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
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
