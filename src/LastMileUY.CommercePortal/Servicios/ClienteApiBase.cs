using System.Net;

namespace LastMileUY.CommercePortal.Servicios;

// Base de los clientes de la API: agrega la cookie de sesión del usuario logueado a cada pedido
// y avisa con SesionVencidaException si la API responde 401.
// Se hace acá y no con un DelegatingHandler porque los handlers viven en otro scope de DI
// y no ven el estado de autenticación del circuito de Blazor.
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
