using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

public static class ReglasOperativasConsultas
{
    public static Task<ReglaOperativa?> ObtenerVigenteAsync(
        this ApplicationDbContext db,
        ModalidadEnvio modalidad,
        CancellationToken cancellationToken = default) =>
        db.ReglasOperativas
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Activa && r.Modalidad == modalidad, cancellationToken);

    public static Task<ReglaEntregaParcial?> ObtenerReglaEntregaParcialVigenteAsync(
        this ApplicationDbContext db,
        CancellationToken cancellationToken = default) =>
        db.ReglasEntregaParcial
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken);
}
