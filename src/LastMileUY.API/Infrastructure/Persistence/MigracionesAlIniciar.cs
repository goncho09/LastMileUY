using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Persistence;

// Aplica las migraciones pendientes al arrancar, si BaseDeDatos:AplicarMigraciones es true
// (lo prende compose.yaml). Usa la cadena "Migraciones", la del dueño de las tablas:
// el usuario de la aplicación no puede crear tablas ni el rol lastmileuy_app.
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
