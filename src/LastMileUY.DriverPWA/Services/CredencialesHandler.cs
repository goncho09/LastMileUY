using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace LastMileUY.DriverPWA.Services;

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
