using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

// Regla operativa vigente del operador actual. No filtra por operador a mano:
// el filtro de EF y el RLS ya dejan ver solo las reglas propias.
public static class ReglasOperativasConsultas
{
    // Hay como máximo una activa por modalidad (índice único filtrado). null si el operador no definió ninguna.
    public static Task<ReglaOperativa?> ObtenerVigenteAsync(
        this ApplicationDbContext db,
        ModalidadEnvio modalidad,
        CancellationToken cancellationToken = default) =>
        db.ReglasOperativas
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Activa && r.Modalidad == modalidad, cancellationToken);
}
