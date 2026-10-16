using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Infrastructure;

// Vehículos y repartidores de los operadores de prueba, para poder armar hojas de ruta.
// Los usuarios con rol Repartidor los crea IdentitySeeder; acá se les crea su ficha de repartidor.
public static class RutasSeeder
{
    public static async Task SeedPruebaAsync(IServiceProvider services, IReadOnlyList<int> operadoresId)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuariosRepartidores = await userManager.GetUsersInRoleAsync("Repartidor");

        var configuration = services.GetRequiredService<IConfiguration>();
        var cadena = configuration.GetConnectionString("DefaultConnection");

        for (var i = 0; i < operadoresId.Count; i++)
        {
            var operadorId = operadoresId[i];

            // Se guarda como ese operador: el filtro de EF y el RLS no dejan crear datos de otro
            var contexto = new ContextoOperadorFijo(operadorId);
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(cadena)
                .AddInterceptors(new InterceptorOperadorConexion(contexto))
                .Options;

            await using var db = new ApplicationDbContext(opciones, contexto);

            if (!await db.Vehiculos.AnyAsync())
            {
                db.Vehiculos.AddRange(
                    new Vehiculo { Matricula = $"MOT {1000 + i}", Tipo = "Moto", CapacidadPesoKg = 30, CapacidadVolumenM3 = 0.15m },
                    new Vehiculo { Matricula = $"FUR {2000 + i}", Tipo = "Furgoneta", CapacidadPesoKg = 800, CapacidadVolumenM3 = 6 });
            }

            var conFicha = await db.Repartidores.Select(r => r.UsuarioId).ToListAsync();

            foreach (var usuario in usuariosRepartidores.Where(u => u.OperadorId == operadorId && !conFicha.Contains(u.Id)))
            {
                db.Repartidores.Add(new Repartidor { UsuarioId = usuario.Id });
            }

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"Vehículos y repartidores del operador {operadorId} inicializados");
            }
        }
    }
}
