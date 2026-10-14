using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Identidad.Infrastructure;

public class IdentitySeeder
{
    
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<Rol>>();

        string[] roles =
        [
            "Administrador",
            "Operador",
            "Comercio",
            "Repartidor",
            "Despachador",
            "OperarioDeposito"
        ];

        foreach (var nombre in roles)
        {
            if (await roleManager.RoleExistsAsync(nombre))
            {
                continue;
            }

            var resultado = await roleManager.CreateAsync(new Rol
            {
                Name = nombre
            });

            if (!resultado.Succeeded)
            {
                var errores = string.Join(
                    ", ",
                    resultado.Errors.Select(e => e.Description));

                throw new InvalidOperationException(
                    $"No se pudo crear el rol '{nombre}': {errores}");
            }
            
        }
        
        Console.WriteLine("Roles inicializados correctamente");
    }
    
    public static async Task SeedAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<Usuario>>();

        var configuration = scope.ServiceProvider
            .GetRequiredService<IConfiguration>();

        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var usuarioExistente = await userManager.FindByEmailAsync(email);

        if (usuarioExistente is not null)
        {
            Console.WriteLine($"El administrador {usuarioExistente.UserName} ya existe");
            return;
        }

        var usuario = new Usuario
        {
            UserName = email,
            Email = email,
            Nombre = "Administrador",
            Apellido = "Sistema",
            EmailConfirmed = true,
            Activo = true
        };

        var resultado = await userManager.CreateAsync(usuario, password);

        if (!resultado.Succeeded)
        {
            var errores = string.Join(
                ", ",
                resultado.Errors.Select(e => e.Description));

            throw new InvalidOperationException(
                $"No se pudo crear el administrador: {errores}");
        }

        var resultadoRol = await userManager.AddToRoleAsync(
            usuario, "Administrador");

        if (!resultadoRol.Succeeded)
        {
            var errores = string.Join(
                ", ",
                resultadoRol.Errors.Select(e => e.Description));

            throw new InvalidOperationException(
                $"No se pudo asignar el rol Administrador: {errores}");
        }
        
        Console.WriteLine("Admin inicializado correctamente");
        
    }
    
    // Crea dos operadores de prueba (si no existen) y devuelve sus Id.
    // Se buscan por nombre y el Id lo pone la base: si se fijara a mano,
    // la secuencia no avanza y el próximo operador creado chocaría con ese Id.
    public static async Task<int[]> SeedOperadoresPruebaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var operadores = new List<Operador>();

        for (var numero = 1; numero <= 2; numero++)
        {
            var nombre = $"Operador Prueba {numero}";

            var operador = await db.Set<Operador>()
                .FirstOrDefaultAsync(o => o.Nombre == nombre);

            if (operador is null)
            {
                operador = new Operador
                {
                    Nombre = nombre,
                    Activo = true
                };

                db.Set<Operador>().Add(operador);
            }

            operadores.Add(operador);
        }

        await db.SaveChangesAsync();

        return operadores.Select(o => o.Id).ToArray();
    }

    public static async Task SeedOperadorPruebaAsync(IServiceProvider services)
    {
        var operadoresId = await SeedOperadoresPruebaAsync(services);
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<Usuario>>();

        var configuration = scope.ServiceProvider
            .GetRequiredService<IConfiguration>();

        var email = configuration["SeedOperadorPrueba:Email"];
        var password = configuration["SeedOperadorPrueba:Password"];

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Credenciales del operador no configuradas");
            return;
        }

        for (var i = 0; i < operadoresId.Length; i++)
        {
            var operadorId = operadoresId[i];
            var emailOperador = i == 0
                ? email
                : email.Replace("@", "2@");

            var usuario = await userManager.FindByEmailAsync(emailOperador);

            if (usuario is not null)
                continue;

            usuario = new Usuario
            {
                UserName = emailOperador,
                Email = emailOperador,
                Nombre = "Operador",
                Apellido = $"Prueba {i + 1}",
                OperadorId = operadorId,
                EmailConfirmed = true,
                Activo = true
            };

            var resultado = await userManager.CreateAsync(usuario, password);

            if (!resultado.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join(", ", resultado.Errors.Select(e => e.Description)));
            }

            var resultadoRol = await userManager.AddToRoleAsync(usuario, "Operador");

            if (!resultadoRol.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join(", ", resultadoRol.Errors.Select(e => e.Description)));
            }

            Console.WriteLine($"Operador {operadorId} inicializado correctamente");
        }
    }
}