using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Application;

// El orden sugerido para las paradas de una ruta que se está armando (POST /api/rutas/sugerir-orden).
// No guarda nada: el despachador lo ve, lo cambia si quiere y después arma la ruta como siempre.
// El cálculo está en OrdenDeParadas (Contracts, para que el Backoffice de prueba use el mismo).
// Igual que al armar la ruta, un envío de otro operador "no existe" (EF + RLS).
public class SugerirOrdenService(ApplicationDbContext db, IContextoOperador contexto)
{
    public async Task<SugerenciaOrdenDto> SugerirAsync(
        SugerirOrdenRequest request,
        CancellationToken cancellationToken = default)
    {
        var envioIds = request.EnvioIds ?? [];

        if (envioIds.Count == 0)
            throw new ArgumentException("Elegí al menos un envío para ordenar.");

        if (envioIds.Count > PlanificarRutaService.MaximoParadas)
            throw new ArgumentException($"Una ruta puede tener hasta {PlanificarRutaService.MaximoParadas} paradas.");

        var repetido = envioIds.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);

        if (repetido is not null)
            throw new ArgumentException($"El envío {repetido.Key} está más de una vez en la lista.");

        var envios = await (
                from e in db.Envios.AsNoTracking()
                join f in db.FranjasHorarias on e.FranjaHorariaId equals f.Id into franjas
                from f in franjas.DefaultIfEmpty()
                where envioIds.Contains(e.Id)
                select new
                {
                    e.Id,
                    e.CodigoSeguimiento,
                    e.DireccionEntrega.Latitud,
                    e.DireccionEntrega.Longitud,
                    InicioFranja = f == null ? (TimeOnly?)null : f.HoraInicio
                })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var faltante = envioIds.FirstOrDefault(id => !envios.ContainsKey(id));

        if (faltante != 0)
            throw new ArgumentException($"El envío {faltante} no existe.");

        var paradas = envioIds
            .Select(id => envios[id])
            .Select(e => new ParadaAOrdenar(
                e.Id,
                e.Latitud is { } latitud && e.Longitud is { } longitud ? new Coordenadas(latitud, longitud) : null,
                e.InicioFranja))
            .ToList();

        var deposito = await DepositoAsync(cancellationToken);
        var orden = OrdenDeParadas.Sugerir(paradas, deposito);

        return new SugerenciaOrdenDto(
            orden.Ids,
            orden.KmActual,
            orden.KmSugerido,
            orden.SinUbicacion.Select(id => envios[id].CodigoSeguimiento).ToList(),
            DesdeDeposito: deposito is not null);
    }

    // Operadores no tiene RLS ni filtro de EF: se filtra a mano por el operador actual
    private async Task<Coordenadas?> DepositoAsync(CancellationToken cancellationToken)
    {
        if (contexto.OperadorId is not int operadorId)
            return null;

        var operador = await db.Set<Operador>()
            .AsNoTracking()
            .Where(o => o.Id == operadorId)
            .Select(o => new { o.DepositoLatitud, o.DepositoLongitud })
            .FirstOrDefaultAsync(cancellationToken);

        return operador is { DepositoLatitud: { } latitud, DepositoLongitud: { } longitud }
            ? new Coordenadas(latitud, longitud)
            : null;
    }
}
