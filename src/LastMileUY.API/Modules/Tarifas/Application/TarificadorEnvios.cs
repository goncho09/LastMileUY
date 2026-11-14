using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Tarifas.Application;

public record Cotizacion(int CuadroTarifarioId, int TarifaId, string Tarifa, decimal Costo);

public class TarificadorEnvios(ApplicationDbContext db, TimeProvider reloj)
{
    public async Task<Cotizacion?> CotizarAsync(
        int zonaId,
        ModalidadEnvio modalidad,
        decimal pesoKg,
        decimal volumenM3,
        CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var cuadro = await db.CuadrosTarifarios.AsNoTracking()
            .Where(c => c.VigenciaDesde <= ahora && (c.VigenciaHasta == null || c.VigenciaHasta > ahora))
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(ct);

        if (cuadro is null)
            return null;

        var tarifa = await db.Tarifas.AsNoTracking()
            .Include(t => t.Ajustes)
            .Where(t => t.CuadroTarifarioId == cuadro.Id
                && t.Activa
                && t.ZonaId == zonaId
                && t.Modalidad == modalidad
                && t.PesoMinimoKg < pesoKg && pesoKg <= t.PesoMaximoKg
                && t.VolumenMinimoM3 < volumenM3 && volumenM3 <= t.VolumenMaximoM3)
            .OrderBy(t => t.PesoMaximoKg - t.PesoMinimoKg)
            .ThenBy(t => t.VolumenMaximoM3 - t.VolumenMinimoM3)
            .ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        return tarifa is null
            ? null
            : new Cotizacion(cuadro.Id, tarifa.Id, tarifa.Nombre, Calcular(tarifa, pesoKg));
    }

    public static decimal Calcular(Tarifa tarifa, decimal pesoKg)
    {
        var precio = tarifa.PrecioBase
            + Math.Max(0m, pesoKg - tarifa.PesoMinimoKg) * tarifa.PrecioPorKgAdicional;

        var porcentaje = tarifa.Ajustes
            .Where(a => a.Activo)
            .Sum(a => a.Tipo == TipoAjusteTarifa.Recargo ? a.Porcentaje : -a.Porcentaje);

        precio *= 1 + porcentaje / 100m;

        return Math.Max(0m, decimal.Round(precio, 2, MidpointRounding.AwayFromZero));
    }

    public static decimal VolumenM3(IEnumerable<Bulto> bultos) =>
        bultos.Sum(b => b.LargoCm * b.AnchoCm * b.AltoCm) / 1_000_000m;
}
