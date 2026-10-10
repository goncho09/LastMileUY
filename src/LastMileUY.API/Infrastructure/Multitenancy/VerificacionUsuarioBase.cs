using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Multitenancy;

// Un superusuario (o un usuario con BYPASSRLS) se saltea las políticas RLS sin avisar.
// Al arrancar se revisa con qué usuario se conecta la aplicación y, si es uno de esos, se avisa en el log.
public static class VerificacionUsuarioBase
{
    private const string Sql =
        "SELECT (rolsuper OR rolbypassrls) AS \"Value\" FROM pg_roles WHERE rolname = current_user";

    public static async Task AvisarSiSalteaRlsAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(VerificacionUsuarioBase));

        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var salteaRls = await db.Database.SqlQueryRaw<bool>(Sql).SingleAsync();

            if (salteaRls)
            {
                logger.LogWarning(
                    "La aplicación se conecta a PostgreSQL con un usuario que se saltea RLS. " +
                    "Usá el usuario lastmileuy_app en DefaultConnection para que el aislamiento por operador funcione.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo verificar el usuario de la base al arrancar.");
        }
    }
}
