extern alias Api;

namespace LastMileUY.E2E.Tests;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;
using Npgsql;
using Testcontainers.PostgreSql;
using ProgramApi = Api::Program;

public class AppRepartidorEnNavegador : IAsyncLifetime
{
    public const string Clave = "Prueba1234";
    public const string Operador = "operador@prueba.test";
    public const string Despachador = "despachador@prueba.test";
    public const string Repartidor = "repartidor@prueba.test";

    private readonly PostgreSqlContainer baseDeDatos = new PostgreSqlBuilder("postgres:17").Build();
    private readonly string carpetaApp = Path.Combine(Path.GetTempPath(), "lastmileuy-e2e-app-" + Guid.NewGuid().ToString("N"));
    private readonly string carpetaEvidencias = Path.Combine(Path.GetTempPath(), "lastmileuy-e2e-evidencias-" + Guid.NewGuid().ToString("N"));

    private ApiReal? api;
    private WebApplication? servidorApp;
    private IPlaywright? playwright;

    public IBrowser Navegador { get; private set; } = null!;

    public string DireccionApp { get; private set; } = string.Empty;

    public string DireccionApi { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var publicar = PublicarAppAsync();
        await baseDeDatos.StartAsync();

        api = new ApiReal(baseDeDatos.GetConnectionString(), carpetaEvidencias);
        api.UseKestrel(0);
        api.StartServer();
        DireccionApi = Direccion(api.Services);

        await publicar;
        servidorApp = ServidorApp(Path.Combine(carpetaApp, "wwwroot"), DireccionApi);
        await servidorApp.StartAsync();
        DireccionApp = servidorApp.Urls.First().Replace("127.0.0.1", "localhost").TrimEnd('/');

        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) is var codigo and not 0)
            throw new InvalidOperationException($"No se pudo instalar Chromium para Playwright (código {codigo}).");

        Esperas.Configurar();
        playwright = await Playwright.CreateAsync();
        Navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task<(IBrowserContext Celular, IPage Pagina)> AbrirCelularAsync()
    {
        var celular = await Navegador.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = DireccionApp,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            Locale = "es-UY",
            Geolocation = new Geolocation { Latitude = -34.9011f, Longitude = -56.1645f },
            Permissions = ["geolocation"]
        });

        return (Esperas.Preparar(celular), await celular.NewPageAsync());
    }

    public async Task<HttpClient> SesionApiAsync(string email)
    {
        var cliente = new HttpClient(new CookieAMano()) { BaseAddress = new Uri(DireccionApi + "/") };

        using var login = await cliente.PostAsJsonAsync("api/auth/login", new LoginRequest { Email = email, Password = Clave });
        login.EnsureSuccessStatusCode();

        return cliente;
    }

    public async Task<(int RutaId, int EnvioId, string CodigoBulto)> RutaDespachadaAsync(int diasAdelante)
    {
        var operador = await SesionApiAsync(Operador);
        var despachador = await SesionApiAsync(Despachador);
        var (comercio, destinatario, direccion) = await DestinatarioDePruebaAsync();

        using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = comercio,
            DestinatarioId = destinatario,
            DireccionId = direccion,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10, Descripcion = "Caja" }]
        });
        alta.EnsureSuccessStatusCode();
        var envioId = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;

        using (var deposito = await operador.PatchAsJsonAsync($"api/envios/{envioId}/estado",
                   new CambiarEstadoRequest { Estado = "EnDeposito" }))
        {
            deposito.EnsureSuccessStatusCode();
        }

        var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        using var ruta = await despachador.PostAsJsonAsync("api/rutas", new CrearRutaRequest
        {
            Nombre = $"Ruta del navegador +{diasAdelante}",
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(diasAdelante),
            RepartidorId = opciones.Repartidores[0].Id,
            VehiculoId = opciones.Vehiculos[0].Id,
            EnvioIds = [envioId]
        });
        ruta.EnsureSuccessStatusCode();
        var rutaId = (await ruta.Content.ReadFromJsonAsync<RutaDetalleDto>())!.Id;

        using (var despacho = await despachador.PostAsync($"api/rutas/{rutaId}/despachar", null))
        {
            despacho.EnsureSuccessStatusCode();
        }

        var envio = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!;

        return (rutaId, envioId, envio.Bultos[0].Codigo);
    }

    public async Task<EnvioDetalleDto> EnvioAsync(int envioId)
    {
        var operador = await SesionApiAsync(Operador);
        return (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!;
    }

    private async Task<(int Comercio, int Destinatario, int Direccion)> DestinatarioDePruebaAsync()
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            SELECT c."Id", d."Id", di."Id"
            FROM "Comercios" c
            JOIN "Destinatarios" d ON d."ComercioId" = c."Id"
            JOIN "Direcciones" di ON di."DestinatarioId" = d."Id"
            WHERE c."Nombre" = 'Comercio Prueba 1'
            ORDER BY d."Id", di."Id"
            LIMIT 1
            """, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        await lector.ReadAsync();

        return (lector.GetInt32(0), lector.GetInt32(1), lector.GetInt32(2));
    }

    private async Task PublicarAppAsync()
    {
        var proyecto = Path.Combine(RaizDelRepositorio(), "src", "LastMileUY.DriverPWA", "LastMileUY.DriverPWA.csproj");
        using var proceso = Process.Start(new ProcessStartInfo("dotnet",
            ["publish", proyecto, "-c", "Release", "-o", carpetaApp, "-nologo", "-v", "q"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        await proceso.WaitForExitAsync();

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"No se pudo publicar la app del repartidor:\n{await salida}\n{await errores}");
    }

    private static WebApplication ServidorApp(string archivos, string direccionApi)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpForwarder();

        var app = builder.Build();
        var proveedor = new PhysicalFileProvider(archivos);
        var tipos = new FileExtensionContentTypeProvider();
        tipos.Mappings[".dat"] = "application/octet-stream";
        tipos.Mappings[".webmanifest"] = "application/manifest+json";

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = proveedor });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = proveedor, ContentTypeProvider = tipos });
        app.MapForwarder("/api/{**resto}", direccionApi);
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = proveedor });

        return app;
    }

    private static string Direccion(IServiceProvider servicios) =>
        servicios.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);

        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "LastMileUY.slnx")))
            carpeta = carpeta.Parent;

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró LastMileUY.slnx.");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Navegador is not null)
            await Navegador.DisposeAsync();

        playwright?.Dispose();

        if (servidorApp is not null)
            await servidorApp.DisposeAsync();

        if (api is not null)
            await api.DisposeAsync();

        await baseDeDatos.DisposeAsync();

        foreach (var carpeta in new[] { carpetaApp, carpetaEvidencias })
        {
            if (Directory.Exists(carpeta))
                Directory.Delete(carpeta, recursive: true);
        }
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionAppRepartidorEnNavegador : ICollectionFixture<AppRepartidorEnNavegador>
{
    public const string Nombre = "App del repartidor en el navegador";
}
