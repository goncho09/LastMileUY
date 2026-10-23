namespace LastMileUY.API.Tests.Multitenancy;

using LastMileUY.API.Infrastructure.Correlacion;
using LastMileUY.API.Infrastructure.Mensajeria;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

// Levanta un PostgreSQL real en Docker, aplica todas las migraciones y carga
// dos operadores con un envío cada uno. Se comparte entre las pruebas de la colección.
public class BaseConDosOperadores : IAsyncLifetime
{
    public const int OperadorA = 1;
    public const int OperadorB = 2;
    public const int EnvioDeA = 101;
    public const int EnvioDeB = 102;

    // Segundo nivel (comercios):
    // - el comercio 1 trabaja con A y con B (los envíos 101 y 102 son suyos);
    // - el comercio 2 trabaja solo con A (envío 103 de A);
    // - el comercio 3 trabaja solo con B (no tiene envíos; su destinatario no lo puede ver A).
    public const int ComercioCompartido = 1;
    public const int ComercioSoloDeA = 2;
    public const int ComercioSoloDeB = 3;
    public const int EnvioDeAComercio2 = 103;
    public const int DestinatarioComercio2 = 2;
    public const int DestinatarioComercio3 = 3;

    // Reglas operativas (modalidad Estándar) distintas para cada operador. Para Urgente no hay regla.
    public const string ReglaDeA = "Estándar A";
    public const string ReglaDeB = "Estándar B";
    public const int MaximoIntentosDeA = 1;
    public const int MaximoIntentosDeB = 2;
    public const int PlazoHorasDeA = 48;

    private readonly PostgreSqlContainer contenedor = new PostgreSqlBuilder("postgres:17")
        .Build();

    // Conexión con el usuario de la aplicación (sin privilegios, le aplica RLS)
    public string CadenaAplicacion { get; private set; } = string.Empty;

    // Conexión con el superusuario (solo para preparar los datos)
    public string CadenaAdministrador => contenedor.GetConnectionString();

    // Conexión con el usuario del publicador del outbox (solo lee y marca MensajesOutbox)
    public string CadenaPublicador => new NpgsqlConnectionStringBuilder(CadenaAdministrador)
    {
        Username = "lastmileuy_publicador",
        Password = "lastmileuy_publicador_dev"
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await contenedor.StartAsync();

        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(CadenaAdministrador)
            .Options;

        await using (var db = new ApplicationDbContext(opciones, new ContextoOperadorFijo(null)))
        {
            await db.Database.MigrateAsync();

            // Como superusuario se saltea RLS, así que puede cargar datos de los dos operadores
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Operadores"("Id", "Nombre", "Activo", "FechaCreacion")
                    VALUES ({OperadorA}, 'Operador A', true, now()), ({OperadorB}, 'Operador B', true, now());
                INSERT INTO "Comercios"("Id", "Nombre", "Activo", "FechaCreacion")
                    VALUES ({ComercioCompartido}, 'Comercio', true, now()),
                           ({ComercioSoloDeA}, 'Otro comercio', true, now()),
                           ({ComercioSoloDeB}, 'Comercio de B', true, now());
                INSERT INTO "OperadoresComercios"("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
                    VALUES ({OperadorA}, {ComercioCompartido}, now(), true),
                           ({OperadorB}, {ComercioCompartido}, now(), true),
                           ({OperadorA}, {ComercioSoloDeA}, now(), true),
                           ({OperadorB}, {ComercioSoloDeB}, now(), true);
                INSERT INTO "Destinatarios"("Id", "ComercioId", "Nombre", "Apellido", "FechaCreacion")
                    VALUES (1, {ComercioCompartido}, 'Ana', 'Silva', now()),
                           ({DestinatarioComercio2}, {ComercioSoloDeA}, 'Bruno', 'Pereira', now()),
                           ({DestinatarioComercio3}, {ComercioSoloDeB}, 'Carla', 'Rodríguez', now());
                INSERT INTO "Direcciones"("Id", "DestinatarioId", "Calle", "Numero", "Ciudad", "Departamento", "Activa")
                    VALUES (1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo', true),
                           (3, {DestinatarioComercio3}, 'Colonia', '900', 'Montevideo', 'Montevideo', true);
                INSERT INTO "Envios"("Id", "OperadorId", "ComercioId", "DestinatarioId",
                        "CodigoSeguimiento", "FechaCreacion", "Estado", "Modalidad",
                        "DireccionEntrega_Calle", "DireccionEntrega_Numero",
                        "DireccionEntrega_Ciudad", "DireccionEntrega_Departamento")
                    VALUES ({EnvioDeA}, {OperadorA}, 1, 1,  'CODIGOA', now(), 1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo'),
                           ({EnvioDeB}, {OperadorB}, 1, 1,  'CODIGOB', now(), 1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo'),
                           ({EnvioDeAComercio2}, {OperadorA}, {ComercioSoloDeA}, {DestinatarioComercio2}, 'CODIGOC', now(), 1, 1,
                            'Ejido', '55', 'Montevideo', 'Montevideo');
                INSERT INTO "Bultos"("EnvioId", "Codigo", "PesoKg", "LargoCm", "AnchoCm", "AltoCm")
                    VALUES ({EnvioDeA}, 'BA1', 1, 10, 10, 10), ({EnvioDeB}, 'BB1', 1, 10, 10, 10);
                INSERT INTO "IntentosEntrega"("EnvioId", "NumeroIntento", "FechaHora", "FechaRecepcion", "Exitoso")
                    VALUES ({EnvioDeA}, 1, now(), now(), false), ({EnvioDeB}, 1, now(), now(), false);
                INSERT INTO "ReglasOperativas"("OperadorId", "Nombre", "Modalidad", "MaximoIntentosEntrega",
                        "HorasEntreIntentos", "PlazoEntregaHoras", "HorasParaIniciarDevolucion",
                        "RequiereFirma", "RequiereFotografia", "RequiereDocumentoReceptor", "Activa", "FechaCreacion")
                    VALUES ({OperadorA}, '{ReglaDeA}', 1, {MaximoIntentosDeA}, 24, {PlazoHorasDeA}, 72, true, false, false, true, now()),
                           ({OperadorB}, '{ReglaDeB}', 1, {MaximoIntentosDeB}, 48, 96, 168, false, true, true, true, now());
                """);
        }

        CadenaAplicacion = new NpgsqlConnectionStringBuilder(CadenaAdministrador)
        {
            Username = "lastmileuy_app",
            Password = "lastmileuy_app_dev"
        }.ConnectionString;
    }

    // Un DbContext como el de la aplicación: usuario sin privilegios + interceptor del operador
    // (y del comercio, para simular a un usuario de comercio) + outbox de eventos, con la
    // correlación del pedido que se quiera simular
    public ApplicationDbContext CrearContexto(int? operadorId, int? comercioId = null, string? correlacion = null)
    {
        var contexto = new ContextoOperadorFijo(operadorId, comercioId);

        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(CadenaAplicacion)
            .AddInterceptors(
                new InterceptorOperadorConexion(contexto),
                new OutboxInterceptor(new ContextoCorrelacion { Id = correlacion }, new SenalOutbox()))
            .Options;

        return new ApplicationDbContext(opciones, contexto);
    }

    public Task DisposeAsync() => contenedor.DisposeAsync().AsTask();
}

[CollectionDefinition(Nombre)]
public class ColeccionBaseConDosOperadores : ICollectionFixture<BaseConDosOperadores>
{
    public const string Nombre = "Base con dos operadores";
}
