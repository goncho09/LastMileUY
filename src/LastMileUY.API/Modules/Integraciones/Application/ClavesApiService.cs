using System.Security.Cryptography;
using System.Text;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Integraciones.Domain;
using LastMileUY.Contracts.Integraciones;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Integraciones.Application;

public class ClavesApiService(
    ApplicationDbContext db,
    IContextoOperador contexto,
    AmbientePruebasService ambientePruebas,
    IConfiguration configuracion)
{
    public const string Inicio = "lmuy_";
    public const string InicioProduccion = "lmuy_live_";
    public const string InicioPruebas = "lmuy_test_";

    private const int LargoPrefijo = 16;

    private int MaximoActivas => configuracion.GetValue("ApiPublica:ClavesActivas", 10);

    public static string CalcularHash(string clave) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave))).ToLowerInvariant();

    public async Task<List<ClaveApiDto>> ListarAsync(CancellationToken ct = default)
    {
        ComercioActual();

        return await db.ClavesApi
            .AsNoTracking()
            .OrderBy(c => c.FechaRevocacion != null)
            .ThenByDescending(c => c.Id)
            .Select(c => new ClaveApiDto(c.Id, c.Nombre, c.Prefijo, c.FechaCreacion, c.UltimoUso, c.FechaRevocacion,
                c.EsPrueba ? AmbientesApi.Pruebas : AmbientesApi.Produccion))
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

        var esPrueba = (request.Ambiente ?? AmbientesApi.Produccion) switch
        {
            AmbientesApi.Produccion => false,
            AmbientesApi.Pruebas => true,
            _ => throw new ArgumentException($"El ambiente tiene que ser {AmbientesApi.Produccion} o {AmbientesApi.Pruebas}.")
        };

        if (esPrueba)
            await ambientePruebas.PrepararParaComercioAsync(comercioId, ct);

        var clave = (esPrueba ? InicioPruebas : InicioProduccion) + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var nueva = new ClaveApi
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            Nombre = nombre,
            Prefijo = clave[..LargoPrefijo],
            Hash = CalcularHash(clave),
            CreadaPorId = usuarioId,
            EsPrueba = esPrueba
        };

        db.ClavesApi.Add(nueva);
        await db.SaveChangesAsync(ct);

        return new ClaveApiCreadaDto(Convertir(nueva), clave);
    }

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
        new(c.Id, c.Nombre, c.Prefijo, c.FechaCreacion, c.UltimoUso, c.FechaRevocacion,
            c.EsPrueba ? AmbientesApi.Pruebas : AmbientesApi.Produccion);

    private (int OperadorId, int ComercioId) ComercioActual() =>
        contexto is { OperadorId: { } operadorId, ComercioId: { } comercioId }
            ? (operadorId, comercioId)
            : throw new UnauthorizedAccessException("Solo un comercio puede administrar sus claves de API.");
}
