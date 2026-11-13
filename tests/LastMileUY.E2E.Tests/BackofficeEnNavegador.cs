namespace LastMileUY.E2E.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using ProgramBackoffice = global::Program;

public class BackofficeEnNavegador : WebApplicationFactory<ProgramBackoffice>, IAsyncLifetime
{
    public const string Operador = "operador@prueba.test";
    public const string Despachador = "despachador@prueba.test";
    public const string Administrador = "administrador@prueba.test";
    public const string Clave = "cualquiera";

    private IPlaywright? playwright;

    public IBrowser Navegador { get; private set; } = null!;

    public string Direccion { get; private set; } = string.Empty;

    private readonly string directorioClaves =
        Path.Combine(Path.GetTempPath(), "lastmileuy-e2e-claves-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        UseKestrel(0);
        StartServer();

        Direccion = Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');

        var instalacion = Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (instalacion != 0)
            throw new InvalidOperationException($"No se pudo instalar Chromium para Playwright (código {instalacion}).");

        Esperas.Configurar();
        playwright = await Playwright.CreateAsync();
        Navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Api:UsarDatosDePrueba", "true");
        builder.UseSetting("DataProtection:Directorio", directorioClaves);
        builder.UseSetting("Login:IntentosPorMinuto", "1000");
    }

    public async Task<IPage> IniciarSesionAsync(string email, int ancho = 1280, int alto = 900)
    {
        var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Direccion,
            ViewportSize = new ViewportSize { Width = ancho, Height = alto },
            Locale = "es-UY"
        });
        var pagina = await Esperas.Preparar(contexto).NewPageAsync();

        await pagina.GotoAsync("/Login");
        await pagina.GetByLabel("Correo electrónico").FillAsync(email);
        await pagina.GetByLabel("Contraseña").FillAsync(Clave);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Iniciar sesión" }).ClickAsync();
        await pagina.WaitForURLAsync(url => !url.Contains("/Login", StringComparison.OrdinalIgnoreCase));

        return pagina;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Navegador is not null)
            await Navegador.DisposeAsync();

        playwright?.Dispose();
        await base.DisposeAsync();

        if (Directory.Exists(directorioClaves))
            Directory.Delete(directorioClaves, recursive: true);
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionBackofficeEnNavegador : ICollectionFixture<BackofficeEnNavegador>
{
    public const string Nombre = "Backoffice en el navegador";
}
