using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Persistence;

public static class MigracionesAlIniciar
{
    public static async Task AplicarSiCorrespondeAsync(WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("BaseDeDatos:AplicarMigraciones"))
        {
            return;
        }

        var cadena = app.Configuration.GetConnectionString("Migraciones")
            ?? throw new InvalidOperationException(
                "BaseDeDatos:AplicarMigraciones está activado pero falta la cadena de conexión 'Migraciones'.");

        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cadena)
            .Options;

        await using var db = new ApplicationDbContext(opciones, new ContextoOperadorFijo(null));
        await db.Database.MigrateAsync();

        app.Logger.LogInformation("Migraciones de la base aplicadas.");
    }
}
