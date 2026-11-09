extern alias Api;

namespace LastMileUY.E2E.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using ProgramApi = Api::Program;

// Lo comparten las pruebas de navegador que necesitan la API de verdad (la app del repartidor y el
// portal del comercio)

// La API como arranca en desarrollo (migraciones y seeders), igual que ApiDePuntaAPunta en las
// pruebas de la API: el usuario de la aplicación (lastmileuy_app) y el dueño para las migraciones
internal sealed class ApiReal(string cadenaDueno, string carpetaEvidencias) : WebApplicationFactory<ProgramApi>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cadenaAplicacion = new NpgsqlConnectionStringBuilder(cadenaDueno)
        {
            Username = "lastmileuy_app",
            Password = "lastmileuy_app_dev"
        }.ConnectionString;

        builder.UseEnvironment("Development");
        builder.UseSetting("BaseDeDatos:AplicarMigraciones", "true");
        builder.UseSetting("ConnectionStrings:Migraciones", cadenaDueno);
        builder.UseSetting("ConnectionStrings:DefaultConnection", cadenaAplicacion);
        builder.UseSetting("SeedAdmin:Email", "admin@prueba.test");
        builder.UseSetting("SeedAdmin:Password", AppRepartidorEnNavegador.Clave);
        builder.UseSetting("SeedOperadorPrueba:Email", AppRepartidorEnNavegador.Operador);
        builder.UseSetting("SeedOperadorPrueba:Password", AppRepartidorEnNavegador.Clave);
        builder.UseSetting("SeedComercioPrueba:Email", "comercio@prueba.test");
        builder.UseSetting("SeedComercioPrueba:Password", AppRepartidorEnNavegador.Clave);
        builder.UseSetting("Evidencias:Directorio", carpetaEvidencias);
    }
}

// Guarda las cookies que manda la API y las devuelve en cada pedido (también las Secure, sin https)
internal sealed class CookieAMano() : DelegatingHandler(new HttpClientHandler { UseCookies = false })
{
    private readonly Dictionary<string, string> cookies = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}")));

        var respuesta = await base.SendAsync(request, cancellationToken);

        if (respuesta.Headers.TryGetValues("Set-Cookie", out var nuevas))
        {
            foreach (var cookie in nuevas.Select(c => c.Split(';')[0]))
            {
                var igual = cookie.IndexOf('=');
                cookies[cookie[..igual]] = cookie[(igual + 1)..];
            }
        }

        return respuesta;
    }
}
