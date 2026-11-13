using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace LastMileUY.API.Tests.PuntaAPunta;

public class ApiDePuntaAPunta : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Clave = "Prueba1234";

    public const string Operador1 = "operador@prueba.test";
    public const string Operador2 = "operador2@prueba.test";
    public const string Despachador1 = "despachador@prueba.test";
    public const string Comercio1 = "comercio@prueba.test";
    public const string Comercio2 = "comercio2@prueba.test";

    public const string Repartidor1 = "repartidor@prueba.test";
    public const string Repartidor2 = "repartidor2@prueba.test";

    private readonly PostgreSqlContainer contenedor = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer rabbit = new RabbitMqBuilder("rabbitmq:4.1-alpine").Build();
    private IConnection? conexionRabbit;

    public record EventoRecibido(EstadoEnvioCambiado Evento, string? MessageId, string? CorrelationId, string Cuerpo);

    private readonly ConcurrentQueue<EventoRecibido> eventosRecibidos = new();

    public int IdOperador1 { get; private set; }
    public int IdOperador2 { get; private set; }
    public int IdComercio1 { get; private set; }
    public int IdComercio2 { get; private set; }

    public string DirectorioEvidencias { get; } =
        Path.Combine(Path.GetTempPath(), "lastmileuy-evidencias-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        await Task.WhenAll(contenedor.StartAsync(), rabbit.StartAsync());

        await EscucharEventosAsync();

        _ = Server;

        IdOperador1 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Operadores\" WHERE \"Nombre\" = 'Operador Prueba 1'");
        IdOperador2 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Operadores\" WHERE \"Nombre\" = 'Operador Prueba 2'");
        IdComercio1 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Comercios\" WHERE \"Nombre\" = 'Comercio Prueba 1'");
        IdComercio2 = await ConsultarIdAsync("SELECT \"Id\" FROM \"Comercios\" WHERE \"Nombre\" = 'Comercio Prueba 2'");

        await EjecutarAsync($"""
            INSERT INTO "OperadoresComercios"("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
                VALUES ({IdOperador2}, {IdComercio2}, now(), true)
            """);
    }


    public async Task<long> ConsultarCantidadAsync(
        string sql,
        params NpgsqlParameter[] parametros)
    {
        await using var conexion =
            new NpgsqlConnection(contenedor.GetConnectionString());

        await conexion.OpenAsync();

        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddRange(parametros);

        return Convert.ToInt64(await comando.ExecuteScalarAsync());
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
        builder.UseSetting("ApiPublica:ClavesActivas", "1000");
        builder.UseSetting("ConnectionStrings:RabbitMQ", rabbit.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Outbox", new NpgsqlConnectionStringBuilder(contenedor.GetConnectionString())
        {
            Username = "lastmileuy_publicador",
            Password = "lastmileuy_publicador_dev"
        }.ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = TimeSpan.Zero;
            });
        });
    }



    public async Task<EventoRecibido> EsperarEventoAsync(Func<EstadoEnvioCambiado, bool> condicion)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < limite)
        {
            var recibido = eventosRecibidos.FirstOrDefault(r => condicion(r.Evento));

            if (recibido is not null)
                return recibido;

            await Task.Delay(100);
        }

        throw new TimeoutException("No llegó el evento esperado a RabbitMQ.");
    }

    private async Task EscucharEventosAsync()
    {
        conexionRabbit = await new ConnectionFactory { Uri = new Uri(rabbit.GetConnectionString()) }
            .CreateConnectionAsync();
        var canal = await conexionRabbit.CreateChannelAsync();

        await canal.ExchangeDeclareAsync(TopologiaMensajeria.ExchangeEnvios, ExchangeType.Topic, durable: true);
        var cola = await canal.QueueDeclareAsync(exclusive: true);
        await canal.QueueBindAsync(cola.QueueName, TopologiaMensajeria.ExchangeEnvios, TopologiaMensajeria.PatronEventosEnvio);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, mensaje) =>
        {
            var cuerpo = Encoding.UTF8.GetString(mensaje.Body.Span);
            eventosRecibidos.Enqueue(new EventoRecibido(
                JsonSerializer.Deserialize<EstadoEnvioCambiado>(cuerpo)!,
                mensaje.BasicProperties.MessageId,
                mensaje.BasicProperties.CorrelationId,
                cuerpo));
            return Task.CompletedTask;
        };

        await canal.BasicConsumeAsync(cola.QueueName, autoAck: true, consumer: consumidor);
    }

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

    public HttpClient ClienteSinSesion() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });

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

    public async Task PublicarEnColaAsync(string cola, params (string Cuerpo, string Error)[] mensajes)
    {
        await using var canal = await conexionRabbit!.CreateChannelAsync();
        await canal.QueueDeclareAsync(cola, durable: true, exclusive: false, autoDelete: false);
        await canal.QueuePurgeAsync(cola);

        foreach (var (cuerpo, error) in mensajes)
        {
            var propiedades = new BasicProperties
            {
                MessageId = Guid.NewGuid().ToString(),
                Type = TiposEvento.EnvioEstadoCambiado,
                Headers = new Dictionary<string, object?> { ["x-intentos"] = "3", ["x-ultimo-error"] = error }
            };

            await canal.BasicPublishAsync("", cola, mandatory: false, propiedades, Encoding.UTF8.GetBytes(cuerpo));
        }
    }

    public async Task<long> ContarComoAplicacionAsync(string variables, string sql)
    {
        var cadena = new NpgsqlConnectionStringBuilder(contenedor.GetConnectionString())
        {
            Username = "lastmileuy_app",
            Password = "lastmileuy_app_dev"
        }.ConnectionString;

        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();

        if (!string.IsNullOrWhiteSpace(variables))
        {
            await using var preparar = new NpgsqlCommand(variables, conexion);
            await preparar.ExecuteNonQueryAsync();
        }

        await using var comando = new NpgsqlCommand(sql, conexion);
        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }

    public async Task EjecutarAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(contenedor.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        if (conexionRabbit is not null)
            await conexionRabbit.DisposeAsync();

        await Task.WhenAll(contenedor.DisposeAsync().AsTask(), rabbit.DisposeAsync().AsTask());

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
