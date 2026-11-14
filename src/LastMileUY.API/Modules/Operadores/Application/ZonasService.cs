using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Configuracion;
using LastMileUY.Contracts.Destinatarios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

public class ZonasService(ApplicationDbContext db, IContextoOperador contexto)
{
    public async Task<IReadOnlyList<ZonaDto>> ListarAsync(CancellationToken ct = default)
    {
        var zonas = await db.Zonas.AsNoTracking()
            .Include(z => z.FranjasHorarias)
            .OrderByDescending(z => z.Activa)
            .ThenBy(z => z.Nombre)
            .ToListAsync(ct);

        return zonas.Select(ADto).ToList();
    }

    public async Task<ZonaDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var zona = await db.Zonas.AsNoTracking()
            .Include(z => z.FranjasHorarias)
            .FirstOrDefaultAsync(z => z.Id == id, ct);

        return zona is null ? null : ADto(zona);
    }

    public async Task<ZonaDto> CrearAsync(GuardarZonaRequest request, CancellationToken ct = default)
    {
        var zona = new Zona { OperadorId = contexto.OperadorActual() };
        Aplicar(zona, request);
        await ValidarSinSuperposicionAsync(zona, ct);

        db.Zonas.Add(zona);
        await db.GuardarAsync(ZonaRepetida(zona.Nombre), ct);

        return ADto(zona);
    }

    public async Task<bool> EditarAsync(int id, GuardarZonaRequest request, CancellationToken ct = default)
    {
        var zona = await db.Zonas.FirstOrDefaultAsync(z => z.Id == id, ct);

        if (zona is null)
            return false;

        Aplicar(zona, request);

        if (zona.Activa)
            await ValidarSinSuperposicionAsync(zona, ct);

        await db.GuardarAsync(ZonaRepetida(zona.Nombre), ct);
        return true;
    }

    public async Task<bool> CambiarActivaAsync(int id, bool activa, CancellationToken ct = default)
    {
        var zona = await db.Zonas.FirstOrDefaultAsync(z => z.Id == id, ct);

        if (zona is null)
            return false;

        if (activa && !zona.Activa)
            await ValidarSinSuperposicionAsync(zona, ct);

        zona.Activa = activa;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FranjaHorariaDto?> CrearFranjaAsync(int zonaId, GuardarFranjaHorariaRequest request, CancellationToken ct = default)
    {
        if (!await db.Zonas.AnyAsync(z => z.Id == zonaId, ct))
            return null;

        var franja = new FranjaHoraria { ZonaId = zonaId };
        Aplicar(franja, request);

        db.FranjasHorarias.Add(franja);
        await db.GuardarAsync(FranjaRepetida(franja.Nombre), ct);

        return ADto(franja);
    }

    public async Task<bool> EditarFranjaAsync(int zonaId, int id, GuardarFranjaHorariaRequest request, CancellationToken ct = default)
    {
        var franja = await db.FranjasHorarias.FirstOrDefaultAsync(f => f.Id == id && f.ZonaId == zonaId, ct);

        if (franja is null)
            return false;

        Aplicar(franja, request);
        await db.GuardarAsync(FranjaRepetida(franja.Nombre), ct);
        return true;
    }

    public async Task<bool> CambiarFranjaActivaAsync(int zonaId, int id, bool activa, CancellationToken ct = default)
    {
        var franja = await db.FranjasHorarias.FirstOrDefaultAsync(f => f.Id == id && f.ZonaId == zonaId, ct);

        if (franja is null)
            return false;

        franja.Activa = activa;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Aplicar(Zona zona, GuardarZonaRequest request)
    {
        zona.Nombre = ConfiguracionComun.Requerido(request.Nombre, "el nombre", 150);
        zona.Descripcion = ConfiguracionComun.Opcional(request.Descripcion, "la descripción", 500);

        var departamento = ConfiguracionComun.Opcional(request.Departamento, "el departamento", 100);

        if (departamento is not null)
        {
            departamento = DepartamentosUruguay.Todos
                .FirstOrDefault(d => CoberturaZonas.Normalizar(d) == CoberturaZonas.Normalizar(departamento))
                ?? throw new ArgumentException($"\"{departamento}\" no es un departamento de Uruguay.");
        }

        var localidades = new List<string>();

        foreach (var localidad in request.Localidades ?? [])
        {
            var limpia = ConfiguracionComun.Opcional(localidad, "cada localidad", 100);

            if (limpia is not null && localidades.All(l => CoberturaZonas.Normalizar(l) != CoberturaZonas.Normalizar(limpia)))
                localidades.Add(limpia);
        }

        if (localidades.Count > 0 && departamento is null)
            throw new ArgumentException("Elegí el departamento de las localidades.");

        if (localidades.Count > 100)
            throw new ArgumentException("Una zona puede tener hasta 100 localidades.");

        zona.Departamento = departamento;
        zona.Localidades = localidades;
    }

    private async Task ValidarSinSuperposicionAsync(Zona zona, CancellationToken ct)
    {
        if (zona.Departamento is null)
            return;

        var otras = await db.Zonas.AsNoTracking()
            .Where(z => z.Activa && z.Id != zona.Id && z.Departamento != null)
            .ToListAsync(ct);

        foreach (var otra in otras)
        {
            if (CoberturaZonas.Superposicion(zona, otra) is { } motivo)
                throw new ConflictoConfiguracionException(motivo + " Una dirección no puede quedar en dos zonas.");
        }
    }

    private static void Aplicar(FranjaHoraria franja, GuardarFranjaHorariaRequest request)
    {
        if (request.HoraFin <= request.HoraInicio)
            throw new ArgumentException("La hora de fin tiene que ser posterior a la de inicio.");

        franja.Nombre = ConfiguracionComun.Requerido(request.Nombre, "el nombre", 100);
        franja.HoraInicio = request.HoraInicio;
        franja.HoraFin = request.HoraFin;
    }

    private static string ZonaRepetida(string nombre) => $"Ya existe una zona llamada \"{nombre}\".";

    private static string FranjaRepetida(string nombre) => $"La zona ya tiene una franja llamada \"{nombre}\".";

    private static ZonaDto ADto(Zona zona) =>
        new(
            zona.Id,
            zona.Nombre,
            zona.Descripcion,
            zona.Activa,
            zona.FranjasHorarias
                .OrderBy(f => f.HoraInicio)
                .ThenBy(f => f.Nombre)
                .Select(ADto)
                .ToList())
        {
            Departamento = zona.Departamento,
            Localidades = zona.Localidades
        };

    private static FranjaHorariaDto ADto(FranjaHoraria franja) =>
        new(franja.Id, franja.ZonaId, franja.Nombre, franja.HoraInicio, franja.HoraFin, franja.Activa);
}
