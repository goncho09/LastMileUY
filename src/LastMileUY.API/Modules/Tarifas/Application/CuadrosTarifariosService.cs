using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.Contracts.Configuracion;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Tarifas.Application;

public class CuadrosTarifariosService(ApplicationDbContext db, IContextoOperador contexto, TimeProvider reloj)
{
    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private const string YaRige = "Ese cuadro ya entró en vigencia y no se puede cambiar: creá una versión nueva.";

    public async Task<IReadOnlyList<CuadroTarifarioDto>> ListarAsync(CancellationToken ct = default)
    {
        var cuadros = await db.CuadrosTarifarios.AsNoTracking()
            .Include(c => c.Tarifas)
            .OrderByDescending(c => c.Version)
            .ToListAsync(ct);

        var zonas = await NombresDeZonasAsync(ct);
        return cuadros.Select(c => ADto(c, zonas)).ToList();
    }

    public async Task<CuadroTarifarioDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var cuadro = await db.CuadrosTarifarios.AsNoTracking()
            .Include(c => c.Tarifas)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return cuadro is null ? null : ADto(cuadro, await NombresDeZonasAsync(ct));
    }

    public async Task<CuadroTarifarioDto> CrearAsync(CrearCuadroTarifarioRequest request, CancellationToken ct = default)
    {
        var operadorId = contexto.OperadorActual();

        if (request.VigenciaDesde <= Hoy())
            throw new ArgumentException("El cuadro nuevo tiene que empezar a regir a partir de mañana.");

        var desde = InicioDelDia(request.VigenciaDesde);

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var anterior = await db.CuadrosTarifarios
            .Include(c => c.Tarifas)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(ct);

        if (anterior is not null && desde <= anterior.VigenciaDesde)
        {
            throw new ArgumentException(
                $"Tiene que empezar después del {ADia(anterior.VigenciaDesde):dd/MM/yyyy}, que es cuando empieza la versión {anterior.Version}.");
        }

        var cuadro = new CuadroTarifario
        {
            OperadorId = operadorId,
            Version = (anterior?.Version ?? 0) + 1,
            VigenciaDesde = desde
        };

        if (request.CopiarTarifasDeLaAnterior && anterior is not null)
        {
            foreach (var tarifa in anterior.Tarifas.Where(t => t.Activa))
            {
                cuadro.Tarifas.Add(new Tarifa
                {
                    Nombre = tarifa.Nombre,
                    ZonaId = tarifa.ZonaId,
                    Modalidad = tarifa.Modalidad,
                    PesoMinimoKg = tarifa.PesoMinimoKg,
                    PesoMaximoKg = tarifa.PesoMaximoKg,
                    VolumenMinimoM3 = tarifa.VolumenMinimoM3,
                    VolumenMaximoM3 = tarifa.VolumenMaximoM3,
                    PrecioBase = tarifa.PrecioBase,
                    PrecioPorKgAdicional = tarifa.PrecioPorKgAdicional
                });
            }
        }

        if (anterior is not null)
            anterior.VigenciaHasta = desde;

        db.CuadrosTarifarios.Add(cuadro);
        await db.GuardarAsync("Otra persona creó una versión al mismo tiempo. Probá de nuevo.", ct);
        await transaccion.CommitAsync(ct);

        return ADto(cuadro, await NombresDeZonasAsync(ct));
    }

    public async Task<TarifaDto?> AgregarTarifaAsync(int cuadroId, GuardarTarifaRequest request, CancellationToken ct = default)
    {
        var cuadro = await db.CuadrosTarifarios.FirstOrDefaultAsync(c => c.Id == cuadroId, ct);

        if (cuadro is null)
            return null;

        ValidarEditable(cuadro);

        var tarifa = new Tarifa { CuadroTarifarioId = cuadro.Id };
        await AplicarAsync(tarifa, request, ct);

        db.Tarifas.Add(tarifa);
        await db.SaveChangesAsync(ct);

        return ADto(tarifa, await NombresDeZonasAsync(ct));
    }

    public async Task<bool> EditarTarifaAsync(int cuadroId, int id, GuardarTarifaRequest request, CancellationToken ct = default)
    {
        var tarifa = await TarifaEditableAsync(cuadroId, id, ct);

        if (tarifa is null)
            return false;

        await AplicarAsync(tarifa, request, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CambiarTarifaActivaAsync(int cuadroId, int id, bool activa, CancellationToken ct = default)
    {
        var tarifa = await TarifaEditableAsync(cuadroId, id, ct);

        if (tarifa is null)
            return false;

        tarifa.Activa = activa;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<Tarifa?> TarifaEditableAsync(int cuadroId, int id, CancellationToken ct)
    {
        var cuadro = await db.CuadrosTarifarios.FirstOrDefaultAsync(c => c.Id == cuadroId, ct);

        if (cuadro is null)
            return null;

        var tarifa = await db.Tarifas.FirstOrDefaultAsync(t => t.Id == id && t.CuadroTarifarioId == cuadroId, ct);

        if (tarifa is not null)
            ValidarEditable(cuadro);

        return tarifa;
    }

    private void ValidarEditable(CuadroTarifario cuadro)
    {
        if (Estado(cuadro) != EstadosCuadroTarifario.Programado)
            throw new ConflictoConfiguracionException(YaRige);
    }

    private async Task AplicarAsync(Tarifa tarifa, GuardarTarifaRequest request, CancellationToken ct)
    {
        if (!await db.Zonas.AnyAsync(z => z.Id == request.ZonaId, ct))
            throw new ArgumentException("La zona no existe.");

        tarifa.Nombre = ConfiguracionComun.Requerido(request.Nombre, "el nombre", 150);
        tarifa.ZonaId = request.ZonaId;
        tarifa.Modalidad = ConfiguracionComun.Modalidad(request.Modalidad);
        tarifa.PesoMinimoKg = ConfiguracionComun.Cantidad(request.PesoMinimoKg, "el peso mínimo", 2, 99_999m, permiteCero: true);
        tarifa.PesoMaximoKg = ConfiguracionComun.Cantidad(request.PesoMaximoKg, "el peso máximo", 2, 99_999m);
        tarifa.VolumenMinimoM3 = ConfiguracionComun.Cantidad(request.VolumenMinimoM3, "el volumen mínimo", 3, 999m, permiteCero: true);
        tarifa.VolumenMaximoM3 = ConfiguracionComun.Cantidad(request.VolumenMaximoM3, "el volumen máximo", 3, 999m);
        tarifa.PrecioBase = ConfiguracionComun.Cantidad(request.PrecioBase, "el precio base", 2, 9_999_999m, permiteCero: true);
        tarifa.PrecioPorKgAdicional = ConfiguracionComun.Cantidad(request.PrecioPorKgAdicional, "el precio por kilo adicional", 2, 9_999_999m, permiteCero: true);

        if (tarifa.PesoMinimoKg >= tarifa.PesoMaximoKg)
            throw new ArgumentException("El peso mínimo tiene que ser menor que el máximo.");

        if (tarifa.VolumenMinimoM3 >= tarifa.VolumenMaximoM3)
            throw new ArgumentException("El volumen mínimo tiene que ser menor que el máximo.");
    }

    private Task<Dictionary<int, string>> NombresDeZonasAsync(CancellationToken ct) =>
        db.Zonas.AsNoTracking().ToDictionaryAsync(z => z.Id, z => z.Nombre, ct);

    private string Estado(CuadroTarifario cuadro)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;

        if (cuadro.VigenciaDesde > ahora)
            return EstadosCuadroTarifario.Programado;

        return cuadro.VigenciaHasta is { } hasta && hasta <= ahora
            ? EstadosCuadroTarifario.Vencido
            : EstadosCuadroTarifario.Vigente;
    }

    private DateOnly Hoy() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), HoraUruguay).DateTime);

    private static DateTime InicioDelDia(DateOnly dia) =>
        TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), HoraUruguay);

    private static DateOnly ADia(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), HoraUruguay));

    private CuadroTarifarioDto ADto(CuadroTarifario cuadro, IReadOnlyDictionary<int, string> zonas) =>
        new(
            cuadro.Id,
            cuadro.Version,
            ADia(cuadro.VigenciaDesde),
            cuadro.VigenciaHasta is { } hasta ? ADia(hasta) : null,
            Estado(cuadro),
            cuadro.Tarifas
                .OrderByDescending(t => t.Activa)
                .ThenBy(t => t.Modalidad)
                .ThenBy(t => t.PesoMinimoKg)
                .Select(t => ADto(t, zonas))
                .ToList());

    private static TarifaDto ADto(Tarifa t, IReadOnlyDictionary<int, string> zonas) =>
        new(
            t.Id,
            t.Nombre,
            t.ZonaId,
            zonas.GetValueOrDefault(t.ZonaId, "—"),
            t.Modalidad.ToString(),
            t.PesoMinimoKg,
            t.PesoMaximoKg,
            t.VolumenMinimoM3,
            t.VolumenMaximoM3,
            t.PrecioBase,
            t.PrecioPorKgAdicional,
            t.Activa);
}
