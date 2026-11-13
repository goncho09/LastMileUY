using System.Text.RegularExpressions;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Seguimiento;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Seguimiento.Application;

public partial class SeguimientoPublicoService(ApplicationDbContext db, FuncionalidadesService funcionalidades)
{
    public async Task<SeguimientoPublicoDto?> ObtenerAsync(string codigo, CancellationToken cancellationToken = default)
    {
        codigo = codigo.Trim().ToUpperInvariant();

        if (!FormatoCodigo().IsMatch(codigo))
            return null;

        await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.codigo_seguimiento', {codigo}, true)", cancellationToken);

        var envio = await db.Envios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.CodigoSeguimiento == codigo)
            .Select(e => new
            {
                e.Id,
                e.OperadorId,
                e.CodigoSeguimiento,
                e.Estado,
                e.FechaCreacion,
                e.FechaEntrega,
                e.DireccionEntrega.Ciudad,
                e.DireccionEntrega.Departamento
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (envio is null)
            return null;

        if (!await funcionalidades.EstaActivaAsync(envio.OperadorId, Funcionalidades.SeguimientoPublico, cancellationToken))
            return null;

        var operador = await db.Operadores
            .AsNoTracking()
            .Where(o => o.Id == envio.OperadorId)
            .Select(o => new OperadorPublicoDto(o.Nombre, o.ColorPrimario, o.LogoUrl))
            .SingleAsync(cancellationToken);

        var eventos = await db.EventosEnvio
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ev => ev.EnvioId == envio.Id && ev.EsPublico)
            .OrderBy(ev => ev.Id)
            .Select(ev => new EventoPublicoDto(ev.FechaHora, ev.Tipo))
            .ToListAsync(cancellationToken);

        var estadosBultos = await db.Bultos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.EnvioId == envio.Id)
            .Select(b => b.Estado)
            .ToListAsync(cancellationToken);

        await transaccion.CommitAsync(cancellationToken);

        var bultos = estadosBultos.Count == 0
            ? null
            : new AvanceBultosDto(
                estadosBultos.Count,
                estadosBultos.Count(e => e == EstadoBulto.Entregado),
                estadosBultos.Count(e => e == EstadoBulto.Devuelto));

        return new SeguimientoPublicoDto(
            envio.CodigoSeguimiento,
            envio.Estado.ToString(),
            envio.FechaCreacion,
            envio.FechaEntrega,
            envio.Ciudad,
            envio.Departamento,
            operador,
            eventos,
            bultos);
    }

    [GeneratedRegex("^[A-Z0-9]{4,32}$")]
    private static partial Regex FormatoCodigo();
}
