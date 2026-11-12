using System.Security.Cryptography;
using System.Text;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Integraciones.Domain;
using LastMileUY.Contracts.Integraciones;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Integraciones.Application;

// Las claves de la API pública del comercio logueado (con el operador que eligió en el portal).
// No filtra por comercio a mano: el filtro de EF y el RLS ya dejan ver solo las suyas.
public class ClavesApiService(ApplicationDbContext db, IContextoOperador contexto)
{
    // Comienzo de toda clave: identifica de dónde es si aparece pegada en un log o un repositorio
    public const string Inicio = "lmuy_";

    // Largo del prefijo visible: "lmuy_" y 8 caracteres de la parte secreta
    private const int LargoPrefijo = 13;

    // Tope de claves activas por comercio y operador: una por sistema que se integra, sobra
    public const int MaximoActivas = 10;

    public static string CalcularHash(string clave) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave))).ToLowerInvariant();

    public async Task<List<ClaveApiDto>> ListarAsync(CancellationToken ct = default)
    {
        ComercioActual();

        return await db.ClavesApi
            .AsNoTracking()
            .OrderBy(c => c.FechaRevocacion != null)
            .ThenByDescending(c => c.Id)
            .Select(c => new ClaveApiDto(c.Id, c.Nombre, c.Prefijo, c.FechaCreacion, c.UltimoUso, c.FechaRevocacion))
            .ToListAsync(ct);
    }

    public async Task<ClaveApiCreadaDto> CrearAsync(CrearClaveApiRequest request, string usuarioId, CancellationToken ct = default)
    {
        var (operadorId, comercioId) = ComercioActual();

        var nombre = request.Nombre?.Trim();

        if (string.IsNullOrEmpty(nombre) || nombre.Length > 100)
            throw new ArgumentException("Poné un nombre de hasta 100 caracteres para reconocer la clave.");

        if (await db.ClavesApi.CountAsync(c => c.FechaRevocacion == null, ct) >= MaximoActivas)
            throw new ArgumentException($"Ya tenés {MaximoActivas} claves activas: revocá alguna que no uses.");

        // 32 bytes al azar (256 bits): no se puede adivinar, así que alcanza con un hash rápido para guardarla
        var clave = Inicio + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var nueva = new ClaveApi
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            Nombre = nombre,
            Prefijo = clave[..LargoPrefijo],
            Hash = CalcularHash(clave),
            CreadaPorId = usuarioId
        };

        db.ClavesApi.Add(nueva);
        await db.SaveChangesAsync(ct);

        return new ClaveApiCreadaDto(Convertir(nueva), clave);
    }

    // null si no existe o es de otro comercio (para el que pregunta, es lo mismo)
    public async Task<ClaveApiDto?> RevocarAsync(int id, CancellationToken ct = default)
    {
        ComercioActual();

        var clave = await db.ClavesApi.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (clave is null)
            return null;

        clave.FechaRevocacion ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Convertir(clave);
    }

    // La clave de un pedido, buscada por su hash. Se usa antes de saber de qué comercio es (no hay
    // operador cargado), así que el RLS normal no la dejaría ver: la política clave_api_por_hash
    // (migración ClavesApi) deja ver y tocar solo la fila con el hash cargado en app.hash_clave_api.
    // set_config(..., true) vale solo dentro de esta transacción, igual que en el seguimiento público.
    public static async Task<ClaveApi?> BuscarParaAutenticarAsync(
        ApplicationDbContext db, string hash, CancellationToken ct)
    {
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.hash_clave_api', {hash}, true)", ct);

        var clave = await db.ClavesApi
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Hash == hash && c.FechaRevocacion == null, ct);

        if (clave is null)
            return null;

        var ahora = DateTime.UtcNow;

        if (clave.UltimoUso is null || clave.UltimoUso < ahora.AddMinutes(-1))
        {
            await db.ClavesApi
                .IgnoreQueryFilters()
                .Where(c => c.Id == clave.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimoUso, ahora), ct);
        }

        await transaccion.CommitAsync(ct);
        return clave;
    }

    private static ClaveApiDto Convertir(ClaveApi c) =>
        new(c.Id, c.Nombre, c.Prefijo, c.FechaCreacion, c.UltimoUso, c.FechaRevocacion);

    // Solo un usuario de comercio con un operador elegido
    private (int OperadorId, int ComercioId) ComercioActual() =>
        contexto is { OperadorId: { } operadorId, ComercioId: { } comercioId }
            ? (operadorId, comercioId)
            : throw new UnauthorizedAccessException("Solo un comercio puede administrar sus claves de API.");
}
