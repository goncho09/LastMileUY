using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Envios.Application;

public class CrearEnvioService(
    ApplicationDbContext db,
    IContextoOperador contextoOperador)
{
    public async Task<EnvioResponse> EjecutarAsync(
        CrearEnvioRequest request,
        CancellationToken cancellationToken = default)
    {
        var operadorId = contextoOperador.OperadorId
            ?? throw new UnauthorizedAccessException(
                "No se pudo identificar el operador autenticado.");

        if (request.ComercioId <= 0 ||
            request.DestinatarioId <= 0 ||
            request.DireccionId <= 0)
        {
            throw new ArgumentException(
                "Los identificadores deben ser mayores a cero.");
        }

        var comercioValido = await db.Set<OperadorComercio>()
            .AnyAsync(oc =>
                oc.OperadorId == operadorId &&
                oc.ComercioId == request.ComercioId &&
                oc.Activo &&
                db.Comercios.Any(c =>
                    c.Id == oc.ComercioId && c.Activo),
                cancellationToken);

        if (!comercioValido)
            throw new ArgumentException(
                "El comercio no está activo o no pertenece al operador.");

        var destinatarioValido = await db.Destinatarios
            .AnyAsync(d =>
                d.Id == request.DestinatarioId &&
                d.ComercioId == request.ComercioId,
                cancellationToken);

        if (!destinatarioValido)
            throw new ArgumentException(
                "El destinatario no pertenece al comercio.");

        var direccion = await db.Direcciones
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == request.DireccionId &&
                d.DestinatarioId == request.DestinatarioId &&
                d.Activa,
                cancellationToken);

        if (direccion is null)
            throw new ArgumentException(
                "La dirección no existe, está inactiva o no pertenece al destinatario.");

        var envio = new Envio
        {
            ComercioId = request.ComercioId,
            DestinatarioId = request.DestinatarioId,
            ReferenciaExterna = request.ReferenciaExterna
        };

        envio.AsignarDireccionEntrega(new DireccionEntrega(
            direccion.Calle,
            direccion.Numero,
            direccion.Apartamento,
            direccion.Ciudad,
            direccion.Departamento,
            direccion.CodigoPostal,
            direccion.Referencia
        ));

        db.Envios.Add(envio);
        await db.SaveChangesAsync(cancellationToken);

        return new EnvioResponse
        {
            Id = envio.Id,
            CodigoSeguimiento = envio.CodigoSeguimiento,
            ComercioId = envio.ComercioId,
            DestinatarioId = envio.DestinatarioId,
            Estado = envio.Estado.ToString(),
            FechaCreacion = envio.FechaCreacion
        };
    }
}