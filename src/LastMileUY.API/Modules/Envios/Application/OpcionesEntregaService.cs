using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Envios.Application;

public class OpcionesEntregaService(ApplicationDbContext db)
{
    public async Task<Zona?> ZonaParaAsync(string departamento, string ciudad, CancellationToken ct = default)
    {
        var zonas = await db.Zonas.AsNoTracking()
            .Where(z => z.Activa && z.Departamento != null)
            .ToListAsync(ct);

        return CoberturaZonas.Elegir(zonas, departamento, ciudad);
    }

    public async Task<OpcionesEntregaDto?> ObtenerAsync(int direccionId, CancellationToken ct = default)
    {
        var direccion = await db.Direcciones.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == direccionId && d.Activa, ct);

        if (direccion is null)
            return null;

        var zona = await ZonaParaAsync(direccion.Departamento, direccion.Ciudad, ct);

        if (zona is null)
            return new OpcionesEntregaDto(null, []);

        var franjas = await db.FranjasHorarias.AsNoTracking()
            .Where(f => f.ZonaId == zona.Id && f.Activa)
            .OrderBy(f => f.HoraInicio)
            .ThenBy(f => f.Nombre)
            .Select(f => new FranjaEntregaDto(f.Id, f.Nombre, f.HoraInicio, f.HoraFin))
            .ToListAsync(ct);

        return new OpcionesEntregaDto(zona.Nombre, franjas);
    }
}
