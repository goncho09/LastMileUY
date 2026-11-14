using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Deposito.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.Contracts.Deposito;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Deposito.Application;

public class ConflictoDepositoException(string mensaje) : Exception(mensaje);

public class RecepcionDepositoService(ApplicationDbContext db, TimeProvider reloj)
{
    private const int MaximoListado = 200;

    public async Task<IReadOnlyList<EnvioPorRecibirDto>> PendientesAsync(CancellationToken ct = default) =>
        await (
                from e in db.Envios.AsNoTracking()
                join c in db.Comercios on e.ComercioId equals c.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                where e.Estado == EstadoEnvio.Admitido
                orderby e.FechaCreacion
                select new EnvioPorRecibirDto(
                    e.Id,
                    e.CodigoSeguimiento,
                    c.Nombre,
                    d.Nombre + " " + d.Apellido,
                    e.FechaCreacion,
                    e.Bultos.Count,
                    db.RecepcionesDeposito.Any(r => r.EnvioId == e.Id && !r.Aceptada)))
            .Take(MaximoListado)
            .ToListAsync(ct);

    public async Task<EnvioParaRecibirDto?> BuscarAsync(string codigo, CancellationToken ct = default)
    {
        var limpio = codigo.Trim().ToUpperInvariant();

        if (limpio.Length is 0 or > 40)
            return null;

        var envioId = await db.Envios.AsNoTracking()
            .Where(e => e.CodigoSeguimiento == limpio || e.Bultos.Any(b => b.Codigo == limpio))
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync(ct);

        if (envioId is null)
            return null;

        var envio = await (
                from e in db.Envios.AsNoTracking()
                join c in db.Comercios on e.ComercioId equals c.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                where e.Id == envioId
                select new
                {
                    e.Id,
                    e.CodigoSeguimiento,
                    e.Estado,
                    Comercio = c.Nombre,
                    Destinatario = d.Nombre + " " + d.Apellido,
                    Bultos = e.Bultos
                        .OrderBy(b => b.Id)
                        .Select(b => new BultoPorRecibirDto(b.Id, b.Codigo, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm, b.Descripcion))
                        .ToList()
                })
            .FirstAsync(ct);

        return new EnvioParaRecibirDto(
            envio.Id,
            envio.CodigoSeguimiento,
            envio.Estado.ToString(),
            envio.Comercio,
            envio.Destinatario,
            envio.Bultos,
            await RecepcionesAsync(r => r.EnvioId == envio.Id, ct));
    }

    public async Task<RecepcionDto?> RegistrarAsync(
        RegistrarRecepcionRequest request,
        string? responsableId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observaciones = string.IsNullOrWhiteSpace(request.Observaciones) ? null : request.Observaciones.Trim();

        if (observaciones?.Length > 500)
            throw new ArgumentException("Las observaciones pueden tener hasta 500 caracteres.");

        var envio = await db.Envios
            .Include(e => e.Bultos)
            .FirstOrDefaultAsync(e => e.Id == request.EnvioId, ct);

        if (envio is null)
            return null;

        if (envio.Estado != EstadoEnvio.Admitido)
            throw new ConflictoDepositoException(
                $"El envío {envio.CodigoSeguimiento} ya no está esperando ingreso (está {envio.Estado}).");

        var bultos = ValidarBultos(envio, request.Bultos ?? []);
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var recepcion = new RecepcionDeposito
        {
            OperadorId = envio.OperadorId,
            ComercioId = envio.ComercioId,
            EnvioId = envio.Id,
            FechaHora = ahora,
            ResponsableId = responsableId,
            Observaciones = observaciones,
            Bultos = bultos
        };

        recepcion.TieneDiscrepancias = bultos.Any(b => b.TieneDiscrepancia);
        recepcion.Aceptada = bultos.All(b => b.Estado != EstadoBultoRecibido.Faltante);

        if (recepcion.Aceptada)
        {
            envio.CambiarEstado(EstadoEnvio.EnDeposito, "Deposito", "Ingresó al depósito.", responsableId);

            var incompletas = await db.RecepcionesDeposito
                .Where(r => r.EnvioId == envio.Id && !r.Aceptada && r.TieneDiscrepancias && r.FechaResolucion == null)
                .ToListAsync(ct);

            foreach (var incompleta in incompletas)
                incompleta.Resolver("Se completó en una recepción posterior.", responsableId, ahora);
        }

        db.RecepcionesDeposito.Add(recepcion);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictoDepositoException(
                "Otra persona cambió el envío al mismo tiempo. Volvé a buscarlo.");
        }

        return (await RecepcionesAsync(r => r.Id == recepcion.Id, ct)).Single();
    }

    public Task<IReadOnlyList<RecepcionDto>> DiscrepanciasAsync(bool soloPendientes, CancellationToken ct = default) =>
        RecepcionesAsync(r => r.TieneDiscrepancias && (!soloPendientes || r.FechaResolucion == null), ct);

    public async Task<bool> ResolverAsync(int id, string? resolucion, string? responsableId, CancellationToken ct = default)
    {
        var texto = resolucion?.Trim();

        if (string.IsNullOrEmpty(texto))
            throw new ArgumentException("Contá cómo se resolvió la discrepancia.");

        if (texto.Length > 500)
            throw new ArgumentException("La resolución puede tener hasta 500 caracteres.");

        var recepcion = await db.RecepcionesDeposito.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (recepcion is null)
            return false;

        try
        {
            recepcion.Resolver(texto, responsableId, reloj.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictoDepositoException(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static List<RecepcionBulto> ValidarBultos(Envio envio, IReadOnlyList<BultoRecibidoRequest> pedidos)
    {
        var porCodigo = envio.Bultos.ToDictionary(b => b.Codigo, StringComparer.OrdinalIgnoreCase);
        var resultado = new List<RecepcionBulto>();

        foreach (var pedido in pedidos)
        {
            if (!porCodigo.TryGetValue(pedido.Codigo?.Trim() ?? "", out var bulto))
                throw new ArgumentException($"El bulto {pedido.Codigo} no es de este envío.");

            if (resultado.Any(r => r.BultoId == bulto.Id))
                throw new ArgumentException($"El bulto {bulto.Codigo} está repetido.");

            if (!Enum.TryParse<EstadoBultoRecibido>(pedido.Estado, ignoreCase: true, out var estado) || !Enum.IsDefined(estado))
                throw new ArgumentException($"El estado del bulto {bulto.Codigo} tiene que ser Recibido, Danado o Faltante.");

            if (pedido.PesoMedidoKg is { } peso && (peso <= 0 || peso > 1000 || decimal.Round(peso, 2) != peso))
                throw new ArgumentException($"El peso medido del bulto {bulto.Codigo} tiene que ser mayor que 0, hasta 1000 kg y con 2 decimales.");

            if (estado == EstadoBultoRecibido.Faltante && pedido.PesoMedidoKg is not null)
                throw new ArgumentException($"El bulto {bulto.Codigo} figura como faltante: no puede tener peso medido.");

            resultado.Add(new RecepcionBulto
            {
                BultoId = bulto.Id,
                Estado = estado,
                PesoDeclaradoKg = bulto.PesoKg,
                PesoMedidoKg = pedido.PesoMedidoKg
            });
        }

        var sinInformar = envio.Bultos.Where(b => resultado.All(r => r.BultoId != b.Id)).Select(b => b.Codigo).ToList();

        if (sinInformar.Count > 0)
            throw new ArgumentException($"Falta indicar qué pasó con {string.Join(", ", sinInformar)}.");

        return resultado;
    }

    private async Task<IReadOnlyList<RecepcionDto>> RecepcionesAsync(
        System.Linq.Expressions.Expression<Func<RecepcionDeposito, bool>> filtro,
        CancellationToken ct)
    {
        var filas = await (
                from r in db.RecepcionesDeposito.AsNoTracking().Where(filtro)
                join e in db.Envios on r.EnvioId equals e.Id
                join c in db.Comercios on r.ComercioId equals c.Id
                orderby r.FechaHora descending
                select new
                {
                    Recepcion = r,
                    e.CodigoSeguimiento,
                    Comercio = c.Nombre,
                    Bultos = (
                        from rb in db.RecepcionesBulto
                        join b in db.Bultos on rb.BultoId equals b.Id
                        where rb.RecepcionDepositoId == r.Id
                        orderby b.Id
                        select new { rb.Estado, rb.PesoDeclaradoKg, rb.PesoMedidoKg, b.Codigo })
                        .ToList()
                })
            .Take(MaximoListado)
            .AsSplitQuery()
            .ToListAsync(ct);

        return filas
            .Select(f => new RecepcionDto(
                f.Recepcion.Id,
                f.Recepcion.EnvioId,
                f.CodigoSeguimiento,
                f.Comercio,
                f.Recepcion.FechaHora,
                f.Recepcion.Aceptada,
                f.Recepcion.TieneDiscrepancias,
                f.Recepcion.Observaciones,
                f.Bultos
                    .Select(b =>
                    {
                        var fila = new RecepcionBulto { Estado = b.Estado, PesoDeclaradoKg = b.PesoDeclaradoKg, PesoMedidoKg = b.PesoMedidoKg };
                        return new BultoRecibidoDto(b.Codigo, b.Estado.ToString(), b.PesoDeclaradoKg, b.PesoMedidoKg, fila.PesoDistinto);
                    })
                    .ToList(),
                f.Recepcion.FechaResolucion,
                f.Recepcion.Resolucion))
            .ToList();
    }
}
