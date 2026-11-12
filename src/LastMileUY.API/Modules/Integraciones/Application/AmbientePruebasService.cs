using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Integraciones.Application;

// El ambiente de pruebas de la API pública (docs/api-publica.md) es un operador más, marcado con
// EsAmbientePruebas: los envíos de prueba son suyos, así que quedan aislados de los de verdad por la
// misma barrera que separa a dos operadores (filtro de EF + RLS, ADR-01). No hace falta tocar ninguna
// consulta de envíos, rutas o sincronización: para el operador real, esos envíos no existen.
// Se crea la primera vez que un comercio pide una clave de prueba, y ahí se lo vincula con ese comercio.
// Operadores y OperadoresComercios no tienen RLS (los lee el login antes de saber el operador).
public class AmbientePruebasService(ApplicationDbContext db, AmbientePruebasService.Cache cache)
{
    public const string NombreOperador = "Ambiente de pruebas (API pública)";

    // El Id del operador de pruebas, una vez encontrado: no cambia mientras la API está corriendo
    public sealed class Cache
    {
        public int? OperadorId { get; set; }
    }

    // null si todavía ningún comercio pidió una clave de prueba
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

    // El operador de pruebas, vinculado (y activo) con el comercio. Lo crea si no existe
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
            // Otro pedido lo creó al mismo tiempo (índice único IX_Operadores_UnoDePruebas): se usa ese
            db.Entry(operador).State = EntityState.Detached;
            return await OperadorIdAsync(ct)
                   ?? throw new InvalidOperationException("No se pudo crear el operador del ambiente de pruebas.");
        }
    }
}
