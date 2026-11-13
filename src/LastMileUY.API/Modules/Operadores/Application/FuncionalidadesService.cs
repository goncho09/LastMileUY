using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Operadores;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Operadores.Application;

public class FuncionalidadesService(ApplicationDbContext db)
{
    public static IResult Desactivada(string clave) => Results.Json(
        new { mensaje = $"\"{Funcionalidades.Buscar(clave)?.Nombre ?? clave}\" no está habilitada para tu operador." },
        statusCode: StatusCodes.Status403Forbidden);

    public async Task<IReadOnlySet<string>> ActivasAsync(int operadorId, CancellationToken cancellationToken = default)
    {
        var guardadas = await db.FuncionalidadesOperador
            .AsNoTracking()
            .Where(f => f.OperadorId == operadorId)
            .ToDictionaryAsync(f => f.Clave, f => f.Activa, cancellationToken);

        return Funcionalidades.Todas
            .Where(f => guardadas.GetValueOrDefault(f.Clave, f.PorDefecto))
            .Select(f => f.Clave)
            .ToHashSet();
    }

    public async Task<bool> EstaActivaAsync(int operadorId, string clave, CancellationToken cancellationToken = default)
    {
        var definicion = Funcionalidades.Buscar(clave)
            ?? throw new ArgumentException($"No existe la funcionalidad '{clave}'.", nameof(clave));

        var guardada = await db.FuncionalidadesOperador
            .AsNoTracking()
            .Where(f => f.OperadorId == operadorId && f.Clave == clave)
            .Select(f => (bool?)f.Activa)
            .FirstOrDefaultAsync(cancellationToken);

        return guardada ?? definicion.PorDefecto;
    }

    public async Task<IReadOnlyList<FuncionalidadesOperadorDto>> ListarAsync(CancellationToken cancellationToken = default)
    {
        var operadores = await db.Set<Operador>()
            .AsNoTracking()
            .Where(o => !o.EsAmbientePruebas)
            .OrderBy(o => o.Nombre)
            .Select(o => new { o.Id, o.Nombre })
            .ToListAsync(cancellationToken);

        var guardadas = await db.FuncionalidadesOperador
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return operadores
            .Select(o => new FuncionalidadesOperadorDto(
                o.Id,
                o.Nombre,
                Funcionalidades.Todas
                    .Select(f =>
                    {
                        var guardada = guardadas.FirstOrDefault(g => g.OperadorId == o.Id && g.Clave == f.Clave);
                        return new FuncionalidadDto(f.Clave, f.Nombre, f.Descripcion, guardada?.Activa ?? f.PorDefecto, f.PorDefecto);
                    })
                    .ToList()))
            .ToList();
    }

    public async Task<bool> CambiarAsync(
        int operadorId,
        string clave,
        bool activa,
        string usuarioId,
        CancellationToken cancellationToken = default)
    {
        if (Funcionalidades.Buscar(clave) is null)
        {
            throw new ArgumentException($"No existe la funcionalidad '{clave}'.");
        }

        if (!await db.Set<Operador>().AnyAsync(o => o.Id == operadorId && !o.EsAmbientePruebas, cancellationToken))
        {
            return false;
        }

        try
        {
            await GuardarAsync(operadorId, clave, activa, usuarioId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            await GuardarAsync(operadorId, clave, activa, usuarioId, cancellationToken);
        }

        return true;
    }

    private async Task GuardarAsync(int operadorId, string clave, bool activa, string usuarioId, CancellationToken cancellationToken)
    {
        var guardada = await db.FuncionalidadesOperador
            .FirstOrDefaultAsync(f => f.OperadorId == operadorId && f.Clave == clave, cancellationToken);

        if (guardada is null)
        {
            guardada = new FuncionalidadOperador { OperadorId = operadorId, Clave = clave };
            db.FuncionalidadesOperador.Add(guardada);
        }

        guardada.Activa = activa;
        guardada.FechaModificacion = DateTime.UtcNow;
        guardada.ModificadaPor = usuarioId;

        await db.SaveChangesAsync(cancellationToken);
    }
}
