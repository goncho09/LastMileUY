using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Infrastructure;

public static class ReglasOperativasSeeder
{
    public static async Task SeedPruebaAsync(IServiceProvider services, IReadOnlyList<int> operadoresId)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var cadena = configuration.GetConnectionString("DefaultConnection");

        for (var i = 0; i < operadoresId.Count; i++)
        {
            var contexto = new ContextoOperadorFijo(operadoresId[i]);
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(cadena)
                .AddInterceptors(new InterceptorOperadorConexion(contexto))
                .Options;

            await using var db = new ApplicationDbContext(opciones, contexto);

            if (!await db.ReglasOperativas.AnyAsync())
            {
                db.ReglasOperativas.AddRange(i == 0 ? ReglasOperador1() : ReglasOperador2());
                await db.SaveChangesAsync();

                Console.WriteLine($"Reglas operativas del operador {operadoresId[i]} inicializadas");
            }

            if (!await db.ReglasEntregaParcial.AnyAsync())
            {
                db.ReglasEntregaParcial.Add(ReglaEntregaParcialInicial(operadoresId[i]));
                await db.SaveChangesAsync();

                Console.WriteLine($"Reglas de entrega parcial del operador {operadoresId[i]} inicializadas");
            }
        }
    }

    private static ReglaEntregaParcial ReglaEntregaParcialInicial(int operadorId) => new()
    {
        OperadorId = operadorId,
        Version = 1,
        PermiteEntregaParcial = true,
        DestinoBultosPendientes = DestinoBultosPendientes.NuevaHojaDeRuta,
        Motivo = "Versión inicial de los datos de prueba",
        PublicadaPorId = "datos-de-prueba"
    };

    private static ReglaOperativa[] ReglasOperador1() =>
    [
        new()
        {
            Nombre = "Estándar - Operador 1",
            Modalidad = ModalidadEnvio.Estandar,
            MaximoIntentosEntrega = 2,
            HorasEntreIntentos = 24,
            PlazoEntregaHoras = 48,
            HorasParaIniciarDevolucion = 72,
            RequiereFirma = true
        },
        new()
        {
            Nombre = "Urgente - Operador 1",
            Modalidad = ModalidadEnvio.Urgente,
            MaximoIntentosEntrega = 1,
            HorasEntreIntentos = 4,
            PlazoEntregaHoras = 12,
            HorasParaIniciarDevolucion = 24,
            RequiereFirma = true
        }
    ];

    private static ReglaOperativa[] ReglasOperador2() =>
    [
        new()
        {
            Nombre = "Estándar - Operador 2",
            Modalidad = ModalidadEnvio.Estandar,
            MaximoIntentosEntrega = 3,
            HorasEntreIntentos = 48,
            PlazoEntregaHoras = 96,
            HorasParaIniciarDevolucion = 168,
            RequiereFotografia = true,
            RequiereDocumentoReceptor = true
        },
        new()
        {
            Nombre = "Urgente - Operador 2",
            Modalidad = ModalidadEnvio.Urgente,
            MaximoIntentosEntrega = 2,
            HorasEntreIntentos = 8,
            PlazoEntregaHoras = 24,
            HorasParaIniciarDevolucion = 48,
            RequiereFotografia = true,
            RequiereDocumentoReceptor = true
        }
    ];
}
