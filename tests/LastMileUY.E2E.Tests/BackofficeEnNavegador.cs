namespace LastMileUY.E2E.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
// Playwright también tiene una clase Program: esta es la del Backoffice
using ProgramBackoffice = global::Program;

// El Backoffice de verdad, escuchando en un puerto (Kestrel), con los datos de prueba en memoria
// (Api:UsarDatosDePrueba: no necesita la API ni la base), y un Chromium real que lo usa como una
// persona: hace clic, completa formularios y lee lo que se ve. Cada prueba abre su propio contexto
// del navegador (como una ventana de incógnito): no comparte la sesión con las demás.
//
// La primera vez descarga Chromium (unos 150 MB, en la carpeta de Playwright del usuario); después
// lo reutiliza. En el CI lo instala el paso "Navegador para las pruebas" del workflow.
public class BackofficeEnNavegador : WebApplicationFactory<ProgramBackoffice>, IAsyncLifetime
{
    // Con los datos de prueba cualquier contraseña entra; el email decide el perfil
    // (ver AutenticacionApiDePrueba)
    public const string Operador = "operador@prueba.test";
    public const string Despachador = "despachador@prueba.test";
    public const string Clave = "cualquiera";

    private IPlaywright? playwright;

    public IBrowser Navegador { get; private set; } = null!;

    public string Direccion { get; private set; } = string.Empty;

    // Las claves de la cookie de sesión van a una carpeta temporal, no al proyecto
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

        playwright = await Playwright.CreateAsync();
        Navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Api:UsarDatosDePrueba", "true");
        builder.UseSetting("DataProtection:Directorio", directorioClaves);
    }

    // Una ventana nueva con la sesión iniciada con ese email. ancho: para probar en celular
    public async Task<IPage> IniciarSesionAsync(string email, int ancho = 1280, int alto = 900)
    {
        var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Direccion,
            ViewportSize = new ViewportSize { Width = ancho, Height = alto },
            Locale = "es-UY"
        });
        var pagina = await contexto.NewPageAsync();

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
