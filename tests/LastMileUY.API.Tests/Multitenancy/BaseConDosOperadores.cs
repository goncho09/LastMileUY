namespace LastMileUY.API.Tests.Multitenancy;

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

    private readonly PostgreSqlContainer contenedor = new PostgreSqlBuilder("postgres:17")
        .Build();

    // Conexión con el usuario de la aplicación (sin privilegios, le aplica RLS)
    public string CadenaAplicacion { get; private set; } = string.Empty;

    // Conexión con el superusuario (solo para preparar los datos)
    public string CadenaAdministrador => contenedor.GetConnectionString();

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
                    VALUES (1, 'Comercio', true, now());
                INSERT INTO "Destinatarios"("Id", "ComercioId", "Nombre", "Apellido", "FechaCreacion")
                    VALUES (1, 1, 'Ana', 'Silva', now());
                INSERT INTO "Direcciones"("Id", "DestinatarioId", "Calle", "Numero", "Ciudad", "Departamento", "Activa")
                    VALUES (1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo', true);
                INSERT INTO "Envios"("Id", "OperadorId", "ComercioId", "DestinatarioId", "DireccionId",
                        "CodigoSeguimiento", "FechaCreacion", "Estado", "Modalidad",
                        "DireccionEntrega_Calle", "DireccionEntrega_Numero",
                        "DireccionEntrega_Ciudad", "DireccionEntrega_Departamento")
                    VALUES ({EnvioDeA}, {OperadorA}, 1, 1, 1, 'CODIGOA', now(), 1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo'),
                           ({EnvioDeB}, {OperadorB}, 1, 1, 1, 'CODIGOB', now(), 1, 1, 'Rivera', '1234', 'Montevideo', 'Montevideo');
                INSERT INTO "Bultos"("EnvioId", "Codigo", "PesoKg", "LargoCm", "AnchoCm", "AltoCm")
                    VALUES ({EnvioDeA}, 'BA1', 1, 10, 10, 10), ({EnvioDeB}, 'BB1', 1, 10, 10, 10);
                """);
        }

        CadenaAplicacion = new NpgsqlConnectionStringBuilder(CadenaAdministrador)
        {
            Username = "lastmileuy_app",
            Password = "lastmileuy_app_dev"
        }.ConnectionString;
    }

    // Un DbContext como el de la aplicación: usuario sin privilegios + interceptor del operador
    public ApplicationDbContext CrearContexto(int? operadorId)
    {
        var contexto = new ContextoOperadorFijo(operadorId);

        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(CadenaAplicacion)
            .AddInterceptors(new InterceptorOperadorConexion(contexto))
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
