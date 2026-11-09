using System.Text.RegularExpressions;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Seguimiento;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Seguimiento.Application;

// Seguimiento público: el destinatario consulta su envío con el código, sin iniciar sesión.
// Sin sesión no hay operador, así que el filtro de EF y el RLS no dejarían ver ningún envío.
// Para este caso hay una política RLS aparte (migración SeguimientoPublico): deja LEER solo el envío
// cuyo código se cargó en la variable app.codigo_seguimiento, y nada más. Ver docs/seguimiento-publico.md
// Si el operador del envío tiene apagado el seguimiento público (docs/funcionalidades.md), es como si
// el envío no existiera.
public partial class SeguimientoPublicoService(ApplicationDbContext db, FuncionalidadesService funcionalidades)
{
    public async Task<SeguimientoPublicoDto?> ObtenerAsync(string codigo, CancellationToken cancellationToken = default)
    {
        codigo = codigo.Trim().ToUpperInvariant();

        // Un código mal formado no llega a la base; para quien consulta es lo mismo que uno que no existe
        if (!FormatoCodigo().IsMatch(codigo))
            return null;

        // set_config(..., true) vale solo dentro de esta transacción: al terminar, la conexión vuelve
        // al pool sin el código y el próximo pedido no ve este envío.
        await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.codigo_seguimiento', {codigo}, true)", cancellationToken);

        // IgnoreQueryFilters: el filtro de EF pide un operador y acá no hay. La barrera es la política
        // RLS, que solo devuelve el envío con este código.
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

        // Solo el estado y la hora: la descripción puede tener observaciones internas.
        // En el orden en que se registraron, no por la hora: la de una entrega sin conexión es la del
        // celular, y si estaba mal el historial quedaría desordenado y el último no sería el estado actual.
        var eventos = await db.EventosEnvio
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ev => ev.EnvioId == envio.Id && ev.EsPublico)
            .OrderBy(ev => ev.Id)
            .Select(ev => new EventoPublicoDto(ev.FechaHora, ev.Tipo))
            .ToListAsync(cancellationToken);

        // Avance por bultos: solo los estados, para contar (ni códigos ni ids). Los bultos se ven por la
        // política RLS de su envío, igual que los eventos.
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

    // Letras y números (los códigos se generan con 12, ver Envio.GenerarCodigoSeguimiento)
    [GeneratedRegex("^[A-Z0-9]{4,32}$")]
    private static partial Regex FormatoCodigo();
}
