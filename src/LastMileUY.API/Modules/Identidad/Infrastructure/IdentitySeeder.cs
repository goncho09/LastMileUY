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

            // Colores distintos para que se note en el Backoffice con qué operador se entró
            operador.ColorPrimario ??= numero == 1 ? "#0B5CAD" : "#1E7F4F";

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

        // Dominio del email configurado (operador@prueba.test -> @prueba.test), para el despachador
        var dominio = email[email.IndexOf('@')..];

        for (var i = 0; i < operadoresId.Length; i++)
        {
            var operadorId = operadoresId[i];
            var sufijo = i == 0 ? "" : "2";

            await CrearUsuarioPruebaAsync(userManager, email.Replace("@", $"{sufijo}@"), password,
                "Operador", $"Prueba {i + 1}", operadorId, "Operador");

            // Un despachador por operador: trabaja con los envíos pero no ve la configuración del operador
            await CrearUsuarioPruebaAsync(userManager, $"despachador{sufijo}{dominio}", password,
                "Despachador", $"Prueba {i + 1}", operadorId, "Despachador");
        }
    }

    private static async Task CrearUsuarioPruebaAsync(
        UserManager<Usuario> userManager,
        string email,
        string password,
        string nombre,
        string apellido,
        int operadorId,
        string rol)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var usuario = new Usuario
        {
            UserName = email,
            Email = email,
            Nombre = nombre,
            Apellido = apellido,
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

        var resultadoRol = await userManager.AddToRoleAsync(usuario, rol);

        if (!resultadoRol.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(", ", resultadoRol.Errors.Select(e => e.Description)));
        }

        Console.WriteLine($"{rol} del operador {operadorId} inicializado correctamente");
    }
}