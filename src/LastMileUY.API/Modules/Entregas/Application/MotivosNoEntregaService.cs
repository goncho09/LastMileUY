using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Configuracion;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Entregas.Application;

public class MotivosNoEntregaService(ApplicationDbContext db, IContextoOperador contexto)
{
    public async Task<IReadOnlyList<MotivoNoEntregaConfiguracionDto>> ListarAsync(CancellationToken ct = default) =>
        await db.MotivosNoEntrega.AsNoTracking()
            .OrderByDescending(m => m.Activo)
            .ThenBy(m => m.Nombre)
            .Select(m => new MotivoNoEntregaConfiguracionDto(m.Id, m.Nombre, m.Descripcion, m.Activo))
            .ToListAsync(ct);

    public async Task<MotivoNoEntregaConfiguracionDto> CrearAsync(GuardarMotivoNoEntregaRequest request, CancellationToken ct = default)
    {
        var motivo = new MotivoNoEntrega { OperadorId = contexto.OperadorActual() };
        Aplicar(motivo, request);

        db.MotivosNoEntrega.Add(motivo);
        await db.GuardarAsync(Repetido(motivo.Nombre), ct);

        return new MotivoNoEntregaConfiguracionDto(motivo.Id, motivo.Nombre, motivo.Descripcion, motivo.Activo);
    }

    public async Task<bool> EditarAsync(int id, GuardarMotivoNoEntregaRequest request, CancellationToken ct = default)
    {
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (motivo is null)
            return false;

        Aplicar(motivo, request);
        await db.GuardarAsync(Repetido(motivo.Nombre), ct);
        return true;
    }

    public async Task<bool> CambiarActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (motivo is null)
            return false;

        if (!activo && motivo.Activo && await db.MotivosNoEntrega.CountAsync(m => m.Activo, ct) == 1)
            throw new ArgumentException("Tiene que quedar al menos un motivo activo: el repartidor lo necesita para registrar una entrega fallida.");

        motivo.Activo = activo;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Aplicar(MotivoNoEntrega motivo, GuardarMotivoNoEntregaRequest request)
    {
        motivo.Nombre = ConfiguracionComun.Requerido(request.Nombre, "el nombre", 150);
        motivo.Descripcion = ConfiguracionComun.Opcional(request.Descripcion, "la descripción", 500);
    }

    private static string Repetido(string nombre) => $"Ya existe un motivo llamado \"{nombre}\".";
}
