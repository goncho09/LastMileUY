using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Configuracion;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Application;

public class VehiculosService(ApplicationDbContext db, IContextoOperador contexto)
{
    public async Task<IReadOnlyList<VehiculoConfiguracionDto>> ListarAsync(CancellationToken ct = default) =>
        await db.Vehiculos.AsNoTracking()
            .OrderByDescending(v => v.Activo)
            .ThenBy(v => v.Matricula)
            .Select(v => new VehiculoConfiguracionDto(v.Id, v.Matricula, v.Tipo, v.CapacidadPesoKg, v.CapacidadVolumenM3, v.Activo))
            .ToListAsync(ct);

    public async Task<VehiculoConfiguracionDto> CrearAsync(GuardarVehiculoRequest request, CancellationToken ct = default)
    {
        var vehiculo = new Vehiculo { OperadorId = contexto.OperadorActual() };
        Aplicar(vehiculo, request);

        db.Vehiculos.Add(vehiculo);
        await db.GuardarAsync(Repetido(vehiculo.Matricula), ct);

        return ADto(vehiculo);
    }

    public async Task<bool> EditarAsync(int id, GuardarVehiculoRequest request, CancellationToken ct = default)
    {
        var vehiculo = await db.Vehiculos.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vehiculo is null)
            return false;

        Aplicar(vehiculo, request);
        await db.GuardarAsync(Repetido(vehiculo.Matricula), ct);
        return true;
    }

    public async Task<bool> CambiarActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        var vehiculo = await db.Vehiculos.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vehiculo is null)
            return false;

        vehiculo.Activo = activo;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Aplicar(Vehiculo vehiculo, GuardarVehiculoRequest request)
    {
        vehiculo.Matricula = ConfiguracionComun.Requerido(request.Matricula, "la matrícula", 20).ToUpperInvariant();
        vehiculo.Tipo = ConfiguracionComun.Requerido(request.Tipo, "el tipo", 50);
        vehiculo.CapacidadPesoKg = ConfiguracionComun.Cantidad(request.CapacidadPesoKg, "la capacidad de peso", 2, 99_999_999.99m);
        vehiculo.CapacidadVolumenM3 = ConfiguracionComun.Cantidad(request.CapacidadVolumenM3, "la capacidad de volumen", 3, 9_999_999.999m);
    }

    private static string Repetido(string matricula) => $"Ya hay un vehículo con la matrícula {matricula}.";

    private static VehiculoConfiguracionDto ADto(Vehiculo v) =>
        new(v.Id, v.Matricula, v.Tipo, v.CapacidadPesoKg, v.CapacidadVolumenM3, v.Activo);
}
