using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Integraciones.Application;

public class AmbientePruebasService(ApplicationDbContext db, AmbientePruebasService.Cache cache)
{
    public const string NombreOperador = "Ambiente de pruebas (API pública)";

    public sealed class Cache
    {
        public int? OperadorId { get; set; }
    }

    public async Task<int?> OperadorIdAsync(CancellationToken ct)
    {
        if (cache.OperadorId is { } id)
            return id;

        var encontrado = await db.Operadores
            .AsNoTracking()
            .Where(o => o.EsAmbientePruebas)
            .Select(o => (int?)o.Id)
            .FirstOrDefaultAsync(ct);

        cache.OperadorId = encontrado;
        return encontrado;
    }

    public async Task<int> PrepararParaComercioAsync(int comercioId, CancellationToken ct)
    {
        var operadorId = await OperadorIdAsync(ct) ?? await CrearOperadorAsync(ct);

        var vinculo = await db.OperadoresComercios
            .FirstOrDefaultAsync(v => v.OperadorId == operadorId && v.ComercioId == comercioId, ct);

        if (vinculo is null)
        {
            db.OperadoresComercios.Add(new OperadorComercio
            {
                OperadorId = operadorId,
                ComercioId = comercioId,
                FechaVinculacion = DateTime.UtcNow,
                Activo = true
            });
        }
        else
        {
            vinculo.Activo = true;
        }

        await db.SaveChangesAsync(ct);
        return operadorId;
    }

    private async Task<int> CrearOperadorAsync(CancellationToken ct)
    {
        var operador = new Operador { Nombre = NombreOperador, Activo = true, EsAmbientePruebas = true };
        db.Operadores.Add(operador);

        try
        {
            await db.SaveChangesAsync(ct);
            cache.OperadorId = operador.Id;
            return operador.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(operador).State = EntityState.Detached;
            return await OperadorIdAsync(ct)
                   ?? throw new InvalidOperationException("No se pudo crear el operador del ambiente de pruebas.");
        }
    }
}
