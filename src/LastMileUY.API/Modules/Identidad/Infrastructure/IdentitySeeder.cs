using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
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

    // Crea dos comercios de prueba (si no existen) y devuelve sus Id.
    // Igual que con los operadores: se buscan por nombre y el Id lo pone la base.
    public static async Task<int[]> SeedComerciosPruebaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var comercios = new List<Comercio>();

        for (var numero = 1; numero <= 2; numero++)
        {
            var nombre = $"Comercio Prueba {numero}";

            // IgnoreQueryFilters: Comercio tiene filtro global por operador/comercio y acá no hay
            // ninguno cargado; sin esto nunca lo encontraría y crearía uno nuevo en cada arranque.
            var comercio = await db.Set<Comercio>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Nombre == nombre);

            if (comercio is null)
            {
                comercio = new Comercio
                {
                    Nombre = nombre,
                    Activo = true
                };

                db.Set<Comercio>().Add(comercio);
            }

            comercios.Add(comercio);
        }

        await db.SaveChangesAsync();

        return comercios.Select(c => c.Id).ToArray();
    }

    // Vincula los comercios de prueba con el Operador Prueba 1 (si no lo están).
    // Sin este vínculo el operador no trabaja con esos comercios y no puede crearles envíos.
    // Devuelve el Id del operador y los Id de los comercios, para los pasos siguientes.
    public static async Task<(int OperadorId, int[] ComerciosId)> SeedVinculosPruebaAsync(IServiceProvider services)
    {
        var operadoresId = await SeedOperadoresPruebaAsync(services);
        var comerciosId = await SeedComerciosPruebaAsync(services);
        var operadorId = operadoresId[0];

        using var scope = services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        foreach (var comercioId in comerciosId)
        {
            var yaVinculado = await db.Set<OperadorComercio>()
                .AnyAsync(oc => oc.OperadorId == operadorId && oc.ComercioId == comercioId);

            if (yaVinculado)
                continue;

            db.Set<OperadorComercio>().Add(new OperadorComercio
            {
                OperadorId = operadorId,
                ComercioId = comercioId,
                Activo = true
            });
        }

        await db.SaveChangesAsync();

        Console.WriteLine("Comercios de prueba vinculados al operador correctamente");

        return (operadorId, comerciosId);
    }

    // Crea un usuario con rol Comercio por cada comercio de prueba (si no existe).
    // Solo lleva ComercioId: un usuario es de un operador o de un comercio, nunca de los dos (CK_Usuarios_Ambito).
    public static async Task SeedComercioPruebaAsync(IServiceProvider services)
    {
        var (_, comerciosId) = await SeedVinculosPruebaAsync(services);
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<Usuario>>();

        var configuration = scope.ServiceProvider
            .GetRequiredService<IConfiguration>();

        var email = configuration["SeedComercioPrueba:Email"];
        var password = configuration["SeedComercioPrueba:Password"];

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Credenciales del comercio no configuradas");
            return;
        }

        for (var i = 0; i < comerciosId.Length; i++)
        {
            var comercioId = comerciosId[i];
            var emailComercio = i == 0
                ? email
                : email.Replace("@", "2@");

            var usuario = await userManager.FindByEmailAsync(emailComercio);

            if (usuario is not null)
                continue;

            usuario = new Usuario
            {
                UserName = emailComercio,
                Email = emailComercio,
                Nombre = "Comercio",
                Apellido = $"Prueba {i + 1}",
                ComercioId = comercioId,
                EmailConfirmed = true,
                Activo = true
            };

            var resultado = await userManager.CreateAsync(usuario, password);

            if (!resultado.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join(", ", resultado.Errors.Select(e => e.Description)));
            }

            var resultadoRol = await userManager.AddToRoleAsync(usuario, "Comercio");

            if (!resultadoRol.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join(", ", resultadoRol.Errors.Select(e => e.Description)));
            }

            Console.WriteLine($"Comercio {comercioId} inicializado correctamente");
        }
    }

    // Carga destinatarios con direcciones para cada comercio de prueba (si todavía no tiene).
    // Destinatarios y Direcciones tienen RLS por comercio: con el DbContext normal (sin usuario
    // logueado) PostgreSQL rechaza el insert. Por eso se usa un DbContext "como" cada comercio,
    // igual que hacen las pruebas (ContextoOperadorFijo + InterceptorOperadorConexion).
    public static async Task SeedDestinatariosPruebaAsync(IServiceProvider services)
    {
        var comerciosId = await SeedComerciosPruebaAsync(services);
        using var scope = services.CreateScope();

        var configuration = scope.ServiceProvider
            .GetRequiredService<IConfiguration>();

        var cadena = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta la cadena de conexión DefaultConnection.");

        for (var i = 0; i < comerciosId.Length; i++)
        {
            var contexto = new ContextoOperadorFijo(operadorId: null, comercioId: comerciosId[i]);

            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(cadena)
                .AddInterceptors(new InterceptorOperadorConexion(contexto))
                .Options;

            await using var db = new ApplicationDbContext(opciones, contexto);

            if (await db.Destinatarios.AnyAsync())
                continue;

            db.Destinatarios.AddRange(DestinatariosDePrueba(comerciosId[i], i + 1));
            await db.SaveChangesAsync();

            Console.WriteLine($"Destinatarios del comercio {comerciosId[i]} inicializados correctamente");
        }
    }

    private static List<Destinatario> DestinatariosDePrueba(int comercioId, int numero) =>
    [
        new Destinatario
        {
            ComercioId = comercioId,
            Nombre = "Ana",
            Apellido = $"Silva {numero}",
            Email = $"ana.silva{numero}@ejemplo.uy",
            Telefono = "099123456",
            Direcciones =
            [
                new Direccion
                {
                    Calle = "Av. 18 de Julio", Numero = "1234", Apartamento = "502",
                    Ciudad = "Montevideo", Departamento = "Montevideo", CodigoPostal = "11100"
                },
                new Direccion
                {
                    Calle = "Rivera", Numero = "2500",
                    Ciudad = "Montevideo", Departamento = "Montevideo", CodigoPostal = "11300",
                    Referencia = "Portón verde"
                }
            ]
        },
        new Destinatario
        {
            ComercioId = comercioId,
            Nombre = "Bruno",
            Apellido = $"Pereira {numero}",
            Email = $"bruno.pereira{numero}@ejemplo.uy",
            Telefono = "098765432",
            Direcciones =
            [
                new Direccion
                {
                    Calle = "Av. Giannattasio", Numero = "Km 21",
                    Ciudad = "Ciudad de la Costa", Departamento = "Canelones", CodigoPostal = "15000"
                }
            ]
        }
    ];
}
