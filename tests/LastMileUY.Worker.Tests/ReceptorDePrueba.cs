using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LastMileUY.Worker.Tests;

public sealed class ReceptorDePrueba : IAsyncDisposable
{
    private readonly WebApplicationFactory<AvisoRecibido> fabrica = new();
    private readonly HttpClient consultas;

    public ReceptorDePrueba()
    {
        fabrica.UseKestrel(opciones => opciones.Listen(IPAddress.Loopback, 0));
        fabrica.StartServer();

        var direccion = fabrica.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Direccion = new Uri(direccion);
        consultas = new HttpClient { BaseAddress = Direccion };
    }

    public Uri Direccion { get; }

    public void RegistrarClave(int suscripcionId, string claveFirma)
    {
        fabrica.Services
            .GetRequiredService<RegistroClavesAvisos>()
            .Registrar(suscripcionId, claveFirma);
    }

    public string Url(string ruta) => new Uri(Direccion, ruta).ToString();

    public async Task<List<AvisoRecibido>> RecibidosAsync() =>
        await consultas.GetFromJsonAsync<List<AvisoRecibido>>("avisos") ?? [];

    public async ValueTask DisposeAsync()
    {
        consultas.Dispose();
        await fabrica.DisposeAsync();
    }
}
