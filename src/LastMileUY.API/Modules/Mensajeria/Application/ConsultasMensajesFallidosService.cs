using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Mensajeria.Domain;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Mensajeria.Application;

public class ConsultasMensajesFallidosService(
    ApplicationDbContext db)
{
    public async Task<PaginaResponse<MensajeFallidoResponse>> ListarAsync(
        FiltroMensajesFallidos filtro,
        CancellationToken ct = default)
    {
        var consulta = db.MensajesFallidos
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            if (!Enum.TryParse<EstadoMensajeFallido>(
                    filtro.Estado, true, out var estado)
                || !Enum.IsDefined(estado))
            {
                throw new ArgumentException("Estado de mensaje fallido inválido.");
            }

            consulta = consulta.Where(x => x.Estado == estado);
        }

        if (filtro.Desde is not null)
        {
            consulta = consulta.Where(
                x => x.FechaFallo >= filtro.Desde.Value);
        }

        if (filtro.Hasta is not null)
        {
            consulta = consulta.Where(
                x => x.FechaFallo <= filtro.Hasta.Value);
        }

        var total = await consulta.CountAsync(ct);

        var pagina = Math.Max(1, filtro.Pagina);
        var tamanoPagina = Math.Clamp(filtro.TamanoPagina, 1, 100);

        var registros = await consulta
            .OrderByDescending(x => x.FechaFallo)
            .ThenByDescending(x => x.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync(ct);

        var items = registros.Select(x => new MensajeFallidoResponse(
            x.Id,
            x.MensajeId,
            x.CorrelacionId,
            x.Tipo,
            x.UltimoError,
            x.Intentos,
            x.FechaFallo,
            x.Estado.ToString(),
            x.FechaResolucion
        )).ToList();

        return new PaginaResponse<MensajeFallidoResponse>(
            items,
            pagina,
            tamanoPagina,
            total
        );
    }

    public async Task<MensajeFallidoDetalleResponse?> ObtenerAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var mensaje = await db.MensajesFallidos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (mensaje is null)
            return null;

        return new MensajeFallidoDetalleResponse(
            mensaje.Id,
            mensaje.MensajeId,
            mensaje.CorrelacionId,
            mensaje.Tipo,
            mensaje.Contenido,
            mensaje.UltimoError,
            mensaje.Intentos,
            mensaje.FechaFallo,
            mensaje.Estado.ToString(),
            mensaje.FechaSolicitudReproceso,
            mensaje.SolicitadoPorId,
            mensaje.FechaResolucion
        );
    }
}