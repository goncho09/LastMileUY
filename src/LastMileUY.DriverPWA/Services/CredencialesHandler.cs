using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace LastMileUY.DriverPWA.Services;

// Manda la cookie de sesión en todos los pedidos a la API, aunque la API esté en otro origen
// (en desarrollo). Así cada servicio no tiene que acordarse de pedirlo.
public class CredencialesHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
