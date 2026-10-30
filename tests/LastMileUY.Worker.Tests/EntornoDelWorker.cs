namespace LastMileUY.Worker.Tests;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

// PostgreSQL (con las migraciones de la API) y RabbitMQ reales en Docker, compartidos por las pruebas.
// Datos: dos operadores que trabajan con el mismo comercio, un envío de cada uno y suscripciones del
// comercio con cada operador (activas, inactivas y a otro tipo de evento).
public class EntornoDelWorker : IAsyncLifetime
{
    public const int OperadorA = 1;
    public const int OperadorB = 2;
    public const int Comercio = 1;
    public const int EnvioDeA = 101;
    public const int EnvioDeB = 102;

    // Suscripciones del comercio
    public const int SuscripcionDeA = 1;            // con A, a envio.estado_cambiado, activa
    public const int SuscripcionDeB = 2;            // con B, a envio.estado_cambiado, activa
    public const int SuscripcionInactivaDeA = 3;    // con A, a envio.estado_cambiado, inactiva
    public const int SuscripcionAlAltaDeA = 4;      // con A, a envio.admitido, activa

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer rabbit = new RabbitMqBuilder("rabbitmq:4.1-alpine").Build();

    public string CadenaRabbit => rabbit.GetConnectionString();

    public string CadenaAdministrador => postgres.GetConnectionString();

    // Como en compose: el worker usa el usuario de la aplicación, al que le aplica RLS
    public string CadenaAplicacion => new NpgsqlConnectionStringBuilder(CadenaAdministrador)
    {
        Username = "lastmileuy_app",
        Password = "lastmileuy_app_dev"
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());

        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(CadenaAdministrador).Options;
        await using var db = new ApplicationDbContext(opciones, new ContextoOperadorFijo(null));
        await db.Database.MigrateAsync();
        
        await db.Database.ExecuteSqlInterpolatedAsync($"""
                                                                               INSERT INTO "Operadores"("Id", "Nombre", "Activo", "FechaCreacion")
                                                                               VALUES ({OperadorA}, 'Operador A', true, now()),
                                                                                      ({OperadorB}, 'Operador B', true, now());

                                                                               INSERT INTO "Comercios"("Id", "Nombre", "Activo", "FechaCreacion")
                                                                               VALUES ({Comercio}, 'Comercio', true, now());

                                                                               INSERT INTO "OperadoresComercios"
                                                                                   ("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
                                                                               VALUES ({OperadorA}, {Comercio}, now(), true),
                                                                                      ({OperadorB}, {Comercio}, now(), true);

                                                                               INSERT INTO "Destinatarios"
                                                                                   ("Id", "ComercioId", "Nombre", "Apellido", "FechaCreacion")
                                                                               VALUES (1, {Comercio}, 'Ana', 'Silva', now());

                                                                               INSERT INTO "Envios"
                                                                                   ("Id", "OperadorId", "ComercioId", "DestinatarioId", "CodigoSeguimiento",
                                                                                    "FechaCreacion", "Estado", "Modalidad", "DireccionEntrega_Calle",
                                                                                    "DireccionEntrega_Numero", "DireccionEntrega_Ciudad",
                                                                                    "DireccionEntrega_Departamento")
                                                                               VALUES
                                                                                   ({EnvioDeA}, {OperadorA}, {Comercio}, 1, 'CODIGOA',
                                                                                    now(), 1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo'),
                                                                                   ({EnvioDeB}, {OperadorB}, {Comercio}, 1, 'CODIGOB',
                                                                                    now(), 1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo');

                                                                               INSERT INTO "SuscripcionesAvisos"
                                                                                   ("Id", "OperadorId", "ComercioId", "TipoEvento", "UrlDestino",
                                                                                    "Activa", "FechaCreacion", "ClaveFirma")
                                                                               VALUES
                                                                                   ({SuscripcionDeA}, {OperadorA}, {Comercio},
                                                                                    {TiposEvento.EnvioEstadoCambiado}, 'https://comercio.test/a',
                                                                                    true, now(), encode(gen_random_bytes(32), 'hex')),

                                                                                   ({SuscripcionDeB}, {OperadorB}, {Comercio},
                                                                                    {TiposEvento.EnvioEstadoCambiado}, 'https://comercio.test/b',
                                                                                    true, now(), encode(gen_random_bytes(32), 'hex')),

                                                                                   ({SuscripcionInactivaDeA}, {OperadorA}, {Comercio},
                                                                                    {TiposEvento.EnvioEstadoCambiado}, 'https://comercio.test/vieja',
                                                                                    false, now(), encode(gen_random_bytes(32), 'hex')),

                                                                                   ({SuscripcionAlAltaDeA}, {OperadorA}, {Comercio},
                                                                                    {TiposEvento.EnvioAdmitido}, 'https://comercio.test/altas',
                                                                                    true, now(), encode(gen_random_bytes(32), 'hex'));
                                                                               """);
    }

    // Consulta con el superusuario (saltea RLS), para ver lo que quedó en la base
    public async Task<long> ContarAsync(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = new NpgsqlConnection(CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);

        foreach (var (nombre, valor) in parametros)
            comando.Parameters.AddWithValue(nombre, valor);

        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }

    public static EstadoEnvioCambiado Evento(int operadorId, int envioId, string tipo = TiposEvento.EnvioEstadoCambiado) => new(
        EventoId: Guid.NewGuid(),
        Tipo: tipo,
        Version: EstadoEnvioCambiado.VersionActual,
        OcurridoEn: DateTime.UtcNow,
        OperadorId: operadorId,
        ComercioId: Comercio,
        EnvioId: envioId,
        CodigoSeguimiento: envioId == EnvioDeA ? "CODIGOA" : "CODIGOB",
        Estado: "EnDeposito",
        EstadoAnterior: "Admitido",
        Origen: "Backoffice",
        EsPublico: true,
        CorrelacionId: "prueba-worker");

    public async Task DisposeAsync()
    {
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), rabbit.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionEntornoDelWorker : ICollectionFixture<EntornoDelWorker>
{
    public const string Nombre = "Entorno del worker";
}
