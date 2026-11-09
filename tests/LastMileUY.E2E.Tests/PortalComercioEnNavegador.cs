extern alias Portal;

namespace LastMileUY.E2E.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;
using ProgramPortal = Portal::Program;

// El portal del comercio de verdad, en un Chromium real, hablando con la API de verdad:
// - la API completa, escuchando en un puerto, contra un PostgreSQL en Docker (con los seeders de
//   desarrollo: el comercio de prueba y sus destinatarios);
// - el portal escuchando en otro puerto, con Api:UrlBase apuntando a esa API.
// Cada prueba abre su propio contexto del navegador (como una ventana de incógnito).
public class PortalComercioEnNavegador : IAsyncLifetime
{
    public const string Comercio = "comercio@prueba.test";

    private readonly PostgreSqlContainer baseDeDatos = new PostgreSqlBuilder("postgres:17").Build();
    private readonly string carpetaEvidencias = Path.Combine(Path.GetTempPath(), "lastmileuy-e2e-evidencias-" + Guid.NewGuid().ToString("N"));
    private readonly string carpetaClaves = Path.Combine(Path.GetTempPath(), "lastmileuy-e2e-claves-" + Guid.NewGuid().ToString("N"));

    private ApiReal? api;
    private PortalReal? portal;
    private IPlaywright? playwright;

    public IBrowser Navegador { get; private set; } = null!;

    public string DireccionPortal { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await baseDeDatos.StartAsync();

        api = new ApiReal(baseDeDatos.GetConnectionString(), carpetaEvidencias);
        api.UseKestrel(0);
        api.StartServer();

        portal = new PortalReal(Direccion(api.Services), carpetaClaves);
        portal.UseKestrel(0);
        portal.StartServer();
        DireccionPortal = Direccion(portal.Services);

        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) is var codigo and not 0)
            throw new InvalidOperationException($"No se pudo instalar Chromium para Playwright (código {codigo}).");

        Esperas.Configurar();
        playwright = await Playwright.CreateAsync();
        Navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    // Una ventana nueva con la sesión del comercio de prueba. Ese comercio trabaja con dos operadores:
    // después del login elige el primero
    public async Task<IPage> IniciarSesionAsync()
    {
        var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = DireccionPortal,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Locale = "es-UY"
        });
        var pagina = await Esperas.Preparar(contexto).NewPageAsync();

        await pagina.GotoAsync("/login");
        await pagina.GetByLabel("Email corporativo").FillAsync(Comercio);
        await pagina.GetByLabel("Contraseña").FillAsync(AppRepartidorEnNavegador.Clave);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Iniciar sesión" }).ClickAsync();
        await pagina.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase));

        if (pagina.Url.Contains("/seleccionar-operador", StringComparison.OrdinalIgnoreCase))
        {
            // El radio está oculto: se elige tocando la tarjeta del operador, como una persona
            await pagina.Locator("label:has(input[name='Formulario.OperadorId'])").First.ClickAsync();
            await pagina.GetByRole(AriaRole.Button, new() { Name = "Continuar con el operador" }).ClickAsync();
            await pagina.WaitForURLAsync(url => !url.Contains("/seleccionar-operador", StringComparison.OrdinalIgnoreCase));
        }

        return pagina;
    }

    private static string Direccion(IServiceProvider servicios) =>
        servicios.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Navegador is not null)
            await Navegador.DisposeAsync();

        playwright?.Dispose();

        if (portal is not null)
            await portal.DisposeAsync();

        if (api is not null)
            await api.DisposeAsync();

        await baseDeDatos.DisposeAsync();

        foreach (var carpeta in new[] { carpetaEvidencias, carpetaClaves })
        {
            if (Directory.Exists(carpeta))
                Directory.Delete(carpeta, recursive: true);
        }
    }

    private sealed class PortalReal(string direccionApi, string carpetaClaves) : WebApplicationFactory<ProgramPortal>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Api:UrlBase", direccionApi + "/");
            builder.UseSetting("DataProtection:Directorio", carpetaClaves);
            // Cada prueba inicia sesión, y todas desde la misma IP: el tope de producción (10 por minuto)
            // las frenaría con un 429 cuando sean más
            builder.UseSetting("Login:IntentosPorMinuto", "1000");
        }
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionPortalComercioEnNavegador : ICollectionFixture<PortalComercioEnNavegador>
{
    public const string Nombre = "Portal del comercio en el navegador";
}
