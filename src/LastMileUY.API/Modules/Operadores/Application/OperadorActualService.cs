using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

public class OperadorActualService(
    ApplicationDbContext db,
    IContextoOperador contexto,
    FuncionalidadesService funcionalidades)
{
    public async Task<OperadorActualDto?> ObtenerAsync(CancellationToken cancellationToken = default)
    {
        if (contexto.OperadorId is not int operadorId)
        {
            return null;
        }

        var operador = await db.Set<Operador>()
            .AsNoTracking()
            .Where(o => o.Id == operadorId)
            .Select(o => new { o.Id, o.Nombre, o.ColorPrimario, o.LogoUrl, o.DepositoLatitud, o.DepositoLongitud })
            .FirstOrDefaultAsync(cancellationToken);

        if (operador is null)
        {
            return null;
        }

        var reglas = await db.ReglasOperativas
            .AsNoTracking()
            .Where(r => r.Activa)
            .OrderBy(r => r.Modalidad)
            .Select(r => new ReglaOperativaDto(
                r.Modalidad.ToString(),
                r.Nombre,
                r.Descripcion,
                r.MaximoIntentosEntrega,
                r.HorasEntreIntentos,
                r.PlazoEntregaHoras,
                r.HorasParaIniciarDevolucion,
                r.RequiereFirma,
                r.RequiereFotografia,
                r.RequiereDocumentoReceptor))
            .ToListAsync(cancellationToken);

        var deposito = operador is { DepositoLatitud: { } latitud, DepositoLongitud: { } longitud }
            ? new Coordenadas(latitud, longitud)
            : null;

        var activas = await funcionalidades.ActivasAsync(operador.Id, cancellationToken);

        return new OperadorActualDto(
            operador.Id, operador.Nombre, operador.ColorPrimario, operador.LogoUrl, reglas, deposito,
            activas.Order().ToList());
    }
}
