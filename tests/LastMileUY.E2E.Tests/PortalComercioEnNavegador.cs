extern alias Portal;

namespace LastMileUY.E2E.Tests;

using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Envios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;
using Testcontainers.PostgreSql;
using ProgramPortal = Portal::Program;

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

    public string DireccionApi => direccionApi;

    private string direccionApi = string.Empty;

    public async Task InitializeAsync()
    {
        await baseDeDatos.StartAsync();

        api = new ApiReal(baseDeDatos.GetConnectionString(), carpetaEvidencias);
        api.UseKestrel(0);
        api.StartServer();

        direccionApi = Direccion(api.Services);
        portal = new PortalReal(direccionApi, carpetaClaves);
        portal.UseKestrel(0);
        portal.StartServer();
        DireccionPortal = Direccion(portal.Services);

        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) is var codigo and not 0)
            throw new InvalidOperationException($"No se pudo instalar Chromium para Playwright (código {codigo}).");

        Esperas.Configurar();
        playwright = await Playwright.CreateAsync();
        Navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task<IPage> IniciarSesionAsync(string? operador = null)
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
            var tarjetas = pagina.Locator("label:has(input[name='Formulario.OperadorId'])");
            await (operador is null ? tarjetas.First : tarjetas.Filter(new() { HasText = operador })).ClickAsync();
            await pagina.GetByRole(AriaRole.Button, new() { Name = "Continuar con el operador" }).ClickAsync();
            await pagina.WaitForURLAsync(url => !url.Contains("/seleccionar-operador", StringComparison.OrdinalIgnoreCase));
        }

        return pagina;
    }

    public async Task<EnvioResponse> EnvioDelComercioAsync(string? referencia = null)
    {
        using var operador = new HttpClient(new CookieAMano()) { BaseAddress = new Uri(direccionApi + "/") };
        using (var login = await operador.PostAsJsonAsync("api/auth/login",
                   new LoginRequest { Email = AppRepartidorEnNavegador.Operador, Password = AppRepartidorEnNavegador.Clave }))
        {
            login.EnsureSuccessStatusCode();
        }

        var destino = await ConsultarAsync("""
            SELECT c."Id", d."Id", di."Id"
            FROM "Comercios" c
            JOIN "Destinatarios" d ON d."ComercioId" = c."Id"
            JOIN "Direcciones" di ON di."DestinatarioId" = d."Id"
            WHERE c."Nombre" = 'Comercio Prueba 1'
            ORDER BY d."Id", di."Id"
            LIMIT 1
            """);

        using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = (int)destino[0],
            DestinatarioId = (int)destino[1],
            DireccionId = (int)destino[2],
            ReferenciaExterna = referencia,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10, Descripcion = "Caja" }]
        });
        alta.EnsureSuccessStatusCode();

        return (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!;
    }

    public async Task<object[]> ConsultarAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync();

        if (!await lector.ReadAsync())
            throw new InvalidOperationException("La consulta no devolvió filas.");

        var valores = new object[lector.FieldCount];
        lector.GetValues(valores);
        return valores;
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
            builder.UseSetting("Login:IntentosPorMinuto", "1000");
        }
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionPortalComercioEnNavegador : ICollectionFixture<PortalComercioEnNavegador>
{
    public const string Nombre = "Portal del comercio en el navegador";
}
