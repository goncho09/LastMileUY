using System.Security.Claims;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Envios.Application;

// Cambia el estado de un envío del operador actual. La regla de qué transición vale está en
// el dominio (Envio.CambiarEstado + TransicionesEnvio + la regla operativa del operador);
// acá solo se busca, se aplica y se guarda.
public class CambiarEstadoEnvioService(ApplicationDbContext db)
{
    // Devuelve false si el envío no existe o es de otro operador.
    // ArgumentException: el estado no existe. TransicionInvalidaException: no se puede pasar a ese estado.
    // DbUpdateConcurrencyException: otro usuario lo modificó al mismo tiempo.
    public async Task<bool> EjecutarAsync(
        int id,
        CambiarEstadoRequest request,
        string origen,
        ClaimsPrincipal usuario,
        CancellationToken cancellationToken = default)
    {
        if (usuario.Identity?.IsAuthenticated != true ||
            !(usuario.IsInRole("Operador") ||
              usuario.IsInRole("Despachador")))
        {
            throw new UnauthorizedAccessException(
                "No tenés permisos para cambiar el estado de un envío.");
        }
        
        if (!Enum.TryParse<EstadoEnvio>(request.Estado, ignoreCase: true, out var nuevoEstado)
            || !Enum.IsDefined(nuevoEstado))
        {
            throw new ArgumentException($"El estado '{request.Estado}' no existe.");
        }

        var descripcion = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim();

        if (descripcion?.Length > 1000)
        {
            throw new ArgumentException("El comentario no puede superar los 1000 caracteres.");
        }

        // Los intentos hacen falta para numerar el próximo y para contar los fallidos
        var envio = await db.Envios
            .Include(e => e.IntentosEntrega)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (envio is null)
        {
            return false;
        }

        var regla = await db.ObtenerVigenteAsync(envio.Modalidad, cancellationToken);

        try
        {
          envio.CambiarEstado(
              nuevoEstado,
              origen,
              descripcion,
              usuario.FindFirstValue(ClaimTypes.NameIdentifier),
              regla);
        }
        catch (InvalidOperationException ex)
        {
            throw new TransicionInvalidaException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}

public class TransicionInvalidaException(string mensaje) : Exception(mensaje);
