using System.Net;

namespace LastMileUY.CommercePortal.Servicios;

public abstract class ClienteApiBase(HttpClient http, SesionActual sesion)
{
    protected async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        var cookieApi = await sesion.CookieApiAsync()
            ?? throw new SesionVencidaException();

        pedido.Headers.Remove("Cookie");
        pedido.Headers.Add("Cookie", cookieApi);

        var respuesta = await http.SendAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            respuesta.Dispose();
            throw new SesionVencidaException();
        }

        return respuesta;
    }
}
