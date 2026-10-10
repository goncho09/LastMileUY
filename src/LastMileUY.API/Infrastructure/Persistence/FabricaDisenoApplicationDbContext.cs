using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LastMileUY.API.Infrastructure.Persistence;

// La usan las herramientas de EF (dotnet ef migrations / database update).
// Se conecta con "Migraciones", el usuario dueño de las tablas, porque el usuario
// de la aplicación (DefaultConnection) no puede crear ni cambiar tablas.
public class FabricaDisenoApplicationDbContext : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var entorno = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuracion = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{entorno}.json", optional: true)
            .AddUserSecrets<FabricaDisenoApplicationDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cadena = configuracion.GetConnectionString("Migraciones")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'Migraciones'.");

        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cadena)
            .Options;

        return new ApplicationDbContext(opciones, new ContextoOperadorFijo(null));
    }
}
