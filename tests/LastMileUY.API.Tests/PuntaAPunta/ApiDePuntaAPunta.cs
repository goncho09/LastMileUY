using System.Net;

namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net.Http.Json;
using Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

// La API completa, tal como arranca en desarrollo, contra un PostgreSQL real en Docker:
// aplica las migraciones, corre los seeders (operadores, despachadores, comercios, destinatarios
// reglas, vehículos y repartidores) y se le habla por HTTP iniciando sesión con esos usuarios. Nada se simula.
public class ApiDePuntaAPunta : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Clave = "Prueba1234";

    // Usuarios que crean los seeders de la API (ver IdentitySeeder)
    public const string Operador1 = "operador@prueba.test";
    public const string Operador2 = "operador2@prueba.test";
    public const string Despachador1 = "despachador@prueba.test";
    public const string Comercio1 = "comercio@prueba.test";
    public const string Comercio2 = "comercio2@prueba.test";

    // Un repartidor por operador (IdentitySeeder + RutasSeeder, que le crea la ficha de repartidor)
    public const string Repartidor1 = "repartidor@prueba.test";
    public const string Repartidor2 = "repartidor2@prueba.test";

    private readonly PostgreSqlContainer contenedor = new PostgreSqlBuilder("postgres:17").Build();

    public int IdOperador1 { get; private set; }
    public int IdOperador2 { get; private set; }
    public int IdComercio1 { get; private set; }
    public int IdComercio2 { get; private set; }

    // Las firmas y fotos de las pruebas van a una carpeta temporal, no al proyecto
    public string DirectorioEvidencias { get; } =
        Path.Combine(Path.GetTempPath(), "lastmileuy-evidencias-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        await contenedor.StartAsync();

        // Arranca la API: migraciones + seeders
        _ = Server;

        IdOperador1 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Operadores\" WHERE \"Nombre\" = 'Operador Prueba 1'");
        IdOperador2 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Operadores\" WHERE \"Nombre\" = 'Operador Prueba 2'");
        IdComercio1 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Comercios\" WHERE \"Nombre\" = 'Comercio Prueba 1'");
        IdComercio2 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Comercios\" WHERE \"Nombre\" = 'Comercio Prueba 2'");

        // Los seeders vinculan los dos comercios solo con el operador 1. El comercio 2 pasa a trabajar
        // también con el operador 2, para que el operador 2 tenga envíos propios (y el comercio 2
        // tenga que elegir operador al iniciar sesión).
        await EjecutarAsync($"""
            INSERT INTO "OperadoresComercios"("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
                VALUES ({IdOperador2}, {IdComercio2}, now(), true)
            """);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cadenaAplicacion = new NpgsqlConnectionStringBuilder(contenedor.GetConnectionString())
        {
            Username = "lastmileuy_app",
            Password = "lastmileuy_app_dev"
        }.ConnectionString;

        builder.UseEnvironment("Development");
        builder.UseSetting("BaseDeDatos:AplicarMigraciones", "true");
        builder.UseSetting("ConnectionStrings:Migraciones", contenedor.GetConnectionString());
        builder.UseSetting("ConnectionStrings:DefaultConnection", cadenaAplicacion);
        builder.UseSetting("SeedAdmin:Email", "admin@prueba.test");
        builder.UseSetting("SeedAdmin:Password", Clave);
        builder.UseSetting("SeedOperadorPrueba:Email", Operador1);
        builder.UseSetting("SeedOperadorPrueba:Password", Clave);
        builder.UseSetting("SeedComercioPrueba:Email", Comercio1);
        builder.UseSetting("SeedComercioPrueba:Password", Clave);
        builder.UseSetting("Evidencias:Directorio", DirectorioEvidencias);
    }

    // Un cliente HTTP con sesión iniciada (guarda la cookie como un navegador).
    // operadorId: para un comercio que trabaja con varios operadores.
    
    public async Task<HttpClient> IniciarSesionAsync(string email, int? operadorId = null)
    {
        var cliente = ClienteSinSesion();

        using var respuesta = await cliente.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Email = email,
                Password = Clave,
                OperadorId = operadorId
            });

        if (respuesta.StatusCode == HttpStatusCode.Conflict)
        {
            var seleccion = await respuesta.Content
                .ReadFromJsonAsync<OperadorRequeridoResponse>();

            if (seleccion is null ||
                string.IsNullOrWhiteSpace(seleccion.TokenTemporal))
            {
                throw new InvalidOperationException(
                    "La API no devolvió un token temporal válido.");
            }

            var operadorElegido = operadorId
                                  ?? seleccion.Operadores.FirstOrDefault()?.Id
                                  ?? throw new InvalidOperationException(
                                      "No hay operadores disponibles para seleccionar.");

            using var confirmacion = await cliente.PostAsJsonAsync(
                "api/auth/seleccionar-operador",
                new SeleccionarOperadorRequest
                {
                    TokenTemporal = seleccion.TokenTemporal,
                    OperadorId = operadorElegido
                });

            confirmacion.EnsureSuccessStatusCode();
        }
        else
        {
            respuesta.EnsureSuccessStatusCode();
        }

        return cliente;
    }


    // La cookie de sesión es Secure: se usa https, como en producción
    public HttpClient ClienteSinSesion() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });

    // Primer destinatario (y su primera dirección) de un comercio, leído con el superusuario
    public async Task<(int DestinatarioId, int DireccionId)> DestinatarioDeAsync(int comercioId)
    {
        await using var conexion = new NpgsqlConnection(contenedor.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            SELECT d."Id", di."Id"
            FROM "Destinatarios" d JOIN "Direcciones" di ON di."DestinatarioId" = d."Id"
            WHERE d."ComercioId" = {comercioId}
            ORDER BY d."Id", di."Id"
            LIMIT 1
            """, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        await lector.ReadAsync();

        return (lector.GetInt32(0), lector.GetInt32(1));
    }

    private async Task<int> ConsultarIdAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(contenedor.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);

        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(contenedor.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await contenedor.DisposeAsync();

        if (Directory.Exists(DirectorioEvidencias))
        {
            Directory.Delete(DirectorioEvidencias, recursive: true);
        }
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionApiDePuntaAPunta : ICollectionFixture<ApiDePuntaAPunta>
{
    public const string Nombre = "API de punta a punta";
}
