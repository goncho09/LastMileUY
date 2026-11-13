using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

public class DepositoOperadorService(ApplicationDbContext db, IContextoOperador contexto)
{
    public async Task<bool> GuardarAsync(GuardarDepositoRequest request, CancellationToken cancellationToken = default)
    {
        if (contexto.OperadorId is not int operadorId)
            return false;

        if (request.Latitud is null != request.Longitud is null)
            throw new ArgumentException("El depósito necesita latitud y longitud (o ninguna de las dos, para borrarlo).");

        if (request is { Latitud: { } latitud, Longitud: { } longitud } && !Coordenadas.EsValida(latitud, longitud))
            throw new ArgumentException("La latitud va de -90 a 90 y la longitud de -180 a 180.");

        var operador = await db.Set<Operador>()
            .FirstOrDefaultAsync(o => o.Id == operadorId, cancellationToken);

        if (operador is null)
            return false;

        operador.DepositoLatitud = Redondear(request.Latitud);
        operador.DepositoLongitud = Redondear(request.Longitud);

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static decimal? Redondear(decimal? grados) =>
        grados is { } valor ? Math.Round(valor, 6) : null;
}
