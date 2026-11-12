using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Identidad.Application;

// Operadores con los que trabaja un comercio (vínculo activo en OperadoresComercios y operador activo).
// Se usa en el login: el usuario de comercio no tiene OperadorId propio (CK_Usuarios_Ambito),
// así que elige con qué operador trabaja y la API valida acá que ese vínculo exista.
// OperadoresComercios y Operadores no tienen filtro ni RLS, por eso se puede consultar antes de iniciar sesión.
// El operador del ambiente de pruebas de la API pública no se elige: solo se usa con una clave de prueba.
public class OperadoresDeComercioService(ApplicationDbContext db)
{
    public async Task<List<OperadorDisponibleDto>> ListarAsync(
        int comercioId,
        CancellationToken cancellationToken = default)
    {
        return await (
                from oc in db.OperadoresComercios.AsNoTracking()
                join o in db.Operadores.AsNoTracking() on oc.OperadorId equals o.Id
                where oc.ComercioId == comercioId && oc.Activo && o.Activo && !o.EsAmbientePruebas
                orderby o.Nombre
                select new OperadorDisponibleDto(o.Id, o.Nombre))
            .ToListAsync(cancellationToken);
    }
}
