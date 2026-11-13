using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Destinatarios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Destinatarios.Application;

public class ConsultasDestinatariosService(ApplicationDbContext db)
{
    public async Task<List<DestinatarioDto>> ListarAsync(
        int? comercioId,
        CancellationToken cancellationToken = default)
    {
        var consulta = db.Destinatarios.AsNoTracking();

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
                        x.Referencia,
                        x.Latitud,
                        x.Longitud))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }
}
