using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Entregas.Infrastructure;

public static class MotivosNoEntregaSeeder
{
    private static readonly string[] Motivos =
    [
        "Destinatario ausente",
        "Dirección incorrecta o incompleta",
        "Rechazado por el destinatario",
        "Zona inaccesible"
    ];

    public static async Task SeedPruebaAsync(IServiceProvider services, IReadOnlyList<int> operadoresId)
    {
        var cadena = services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

        foreach (var operadorId in operadoresId)
        {
            var contexto = new ContextoOperadorFijo(operadorId);
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(cadena)
                .AddInterceptors(new InterceptorOperadorConexion(contexto))
                .Options;

            await using var db = new ApplicationDbContext(opciones, contexto);

            if (await db.MotivosNoEntrega.AnyAsync())
            {
                continue;
            }

            db.MotivosNoEntrega.AddRange(Motivos.Select(nombre => new MotivoNoEntrega { Nombre = nombre }));
            await db.SaveChangesAsync();

            Console.WriteLine($"Motivos de no entrega del operador {operadorId} inicializados");
        }
    }
}
