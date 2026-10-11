using LastMileUY.API.Modules.Identidad.Domain;
using Microsoft.AspNetCore.Identity;

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
            "Despachador"
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
            Console.WriteLine($"Usuario {usuarioExistente.UserName} invalido");
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
    
    public static async Task SeedOperadorPruebaAsync(IServiceProvider services)
    {
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

        for (int operadorId = 1; operadorId <= 2; operadorId++)
        {
            var emailOperador = operadorId == 1
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
                Apellido = $"Prueba {operadorId}",
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