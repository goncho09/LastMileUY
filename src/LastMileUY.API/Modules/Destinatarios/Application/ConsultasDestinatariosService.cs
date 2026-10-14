using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Destinatarios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Destinatarios.Application;

// Destinatarios con sus direcciones activas, para elegirlos al dar de alta un envío.
// No filtra por comercio a mano: el filtro global de EF y el RLS ya dejan ver solo
// los del comercio del usuario (o, para el operador, los de sus comercios).
public class ConsultasDestinatariosService(ApplicationDbContext db)
{
    public async Task<List<DestinatarioDto>> ListarAsync(
        int? comercioId,
        CancellationToken cancellationToken = default)
    {
        var consulta = db.Destinatarios.AsNoTracking();

        // Opcional: el operador puede pedir los de un comercio puntual
        if (comercioId is not null)
        {
            consulta = consulta.Where(d => d.ComercioId == comercioId);
        }

        return await consulta
            .OrderBy(d => d.Apellido)
            .ThenBy(d => d.Nombre)
            .Select(d => new DestinatarioDto(
                d.Id,
                d.ComercioId,
                d.Nombre,
                d.Apellido,
                d.Email,
                d.Telefono,
                d.Direcciones
                    .Where(x => x.Activa)
                    .OrderBy(x => x.Id)
                    .Select(x => new DireccionDto(
                        x.Id,
                        x.Calle,
                        x.Numero,
                        x.Apartamento,
                        x.Ciudad,
                        x.Departamento,
                        x.CodigoPostal,
                        x.Referencia))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }
}
