using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Deposito;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Deposito.Application;

public class RendicionService(ApplicationDbContext db, TimeProvider reloj)
{
    private static readonly EstadoRuta[] EstadosParaRendir = [EstadoRuta.Despachada, EstadoRuta.EnCurso, EstadoRuta.Finalizada];

    private static readonly EstadoEnvio[] EstadosCerrados = [EstadoEnvio.Entregado, EstadoEnvio.Devuelto, EstadoEnvio.Extraviado];

    public async Task<IReadOnlyList<RutaPorRendirDto>> PorRendirAsync(CancellationToken ct = default)
    {
        var rutas = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                where EstadosParaRendir.Contains(r.Estado)
                      && !db.Rendiciones.Any(x => x.RutaId == r.Id && x.ConfirmadaEn != null)
                orderby r.Fecha descending, r.Nombre
                select new
                {
                    r.Id,
                    r.Nombre,
                    r.Fecha,
                    r.Estado,
                    Repartidor = u.Nombre + " " + u.Apellido,
                    Declarada = db.Rendiciones.Any(x => x.RutaId == r.Id && x.DeclaradaEn != null)
                })
            .Take(100)
            .ToListAsync(ct);

        var resultado = new List<RutaPorRendirDto>();

        foreach (var ruta in rutas)
        {
            var bultos = await BultosDeLaRutaAsync(ruta.Id, ct);
            var entregados = bultos.Where(b => b.EstadoEnvio == EstadoEnvio.Entregado).Select(b => b.EnvioId).Distinct().Count();

            resultado.Add(new RutaPorRendirDto(ruta.Id, ruta.Nombre, ruta.Fecha, ruta.Repartidor, ruta.Estado.ToString(),
                entregados, bultos.Count(ADevolver),
                ruta.Declarada ? EstadosRendicion.Declarada : EstadosRendicion.SinRendir));
        }

        return resultado;
    }

    public async Task<RendicionRutaDto?> ObtenerAsync(int rutaId, CancellationToken ct = default)
    {
        var ruta = await (
                from r in db.Rutas.AsNoTracking()
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                join u in db.Users on rep.UsuarioId equals u.Id
                where r.Id == rutaId
                select new { r.Id, r.Nombre, r.Fecha, r.Estado, Repartidor = u.Nombre + " " + u.Apellido })
            .FirstOrDefaultAsync(ct);

        if (ruta is null)
            return null;

        var rendicion = await db.Rendiciones.AsNoTracking().FirstOrDefaultAsync(r => r.RutaId == rutaId, ct);
        var bultos = await BultosDeLaRutaAsync(rutaId, ct);
        var envios = bultos.GroupBy(b => b.EnvioId).Select(g => g.First().EstadoEnvio).ToList();
        var declarados = rendicion?.BultosDeclarados.ToHashSet() ?? [];
        var recibidos = rendicion?.BultosRecibidos.ToHashSet() ?? [];

        var estado = rendicion switch
        {
            { ConfirmadaEn: not null } => EstadosRendicion.Confirmada,
            { DeclaradaEn: not null } => EstadosRendicion.Declarada,
            _ => EstadosRendicion.SinRendir
        };

        return new RendicionRutaDto(
            ruta.Id,
            ruta.Nombre,
            ruta.Fecha,
            ruta.Estado.ToString(),
            ruta.Repartidor,
            estado,
            rendicion?.Confirmada == true ? rendicion.CantidadEntregados : envios.Count(e => e == EstadoEnvio.Entregado),
            rendicion?.Confirmada == true ? rendicion.CantidadNoEntregados : envios.Count(e => e != EstadoEnvio.Entregado),
            bultos
                .Where(ADevolver)
                .Select(b => new BultoADevolverDto(b.Codigo, b.EnvioId, b.CodigoSeguimiento, b.Destinatario,
                    b.EstadoEnvio.ToString(), declarados.Contains(b.Codigo),
                    rendicion?.Confirmada == true ? recibidos.Contains(b.Codigo) : null))
                .ToList())
        {
            DeclaradaEn = rendicion?.DeclaradaEn,
            ObservacionesRepartidor = rendicion?.ObservacionesRepartidor,
            ConfirmadaEn = rendicion?.ConfirmadaEn,
            Observaciones = rendicion?.Observaciones,
            Faltantes = rendicion?.Faltantes ?? [],
            Sobrantes = rendicion?.Sobrantes ?? []
        };
    }

    public async Task<RendicionRutaDto?> DeclararAsync(
        int rutaId,
        string usuarioId,
        DeclararRendicionRequest request,
        CancellationToken ct = default)
    {
        var ruta = await (
                from r in db.Rutas
                join rep in db.Repartidores on r.RepartidorId equals rep.Id
                where r.Id == rutaId && rep.UsuarioId == usuarioId
                select r)
            .FirstOrDefaultAsync(ct);

        if (ruta is null)
            return null;

        if (!EstadosParaRendir.Contains(ruta.Estado))
            throw new ConflictoDepositoException("Solo se rinde una hoja de ruta que ya salió.");

        var observaciones = Observaciones(request.Observaciones);
        var codigos = Normalizar(request.BultosDevueltos);
        var rendicion = await db.Rendiciones.FirstOrDefaultAsync(r => r.RutaId == rutaId, ct);

        if (rendicion?.Confirmada == true)
            throw new ConflictoDepositoException("El depósito ya confirmó la rendición de esta hoja de ruta.");

        if (rendicion is null)
        {
            rendicion = new Rendicion { RutaId = rutaId };
            db.Rendiciones.Add(rendicion);
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var enElDispositivo = request.DeclaradaEn is { } cuando
            ? (cuando.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(cuando, DateTimeKind.Utc) : cuando.ToUniversalTime())
            : ahora;

        rendicion.DeclaradaEn = enElDispositivo < ahora ? enElDispositivo : ahora;
        rendicion.ObservacionesRepartidor = observaciones;
        rendicion.BultosDeclarados = codigos.Order().ToList();

        await GuardarAsync(ct);
        return await ObtenerAsync(rutaId, ct);
    }

    public async Task<RendicionRutaDto?> ConfirmarAsync(
        int rutaId,
        ConfirmarRendicionRequest request,
        string? responsableId,
        CancellationToken ct = default)
    {
        var observaciones = Observaciones(request.Observaciones);
        var recibidos = Normalizar(request.BultosRecibidos);

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var ruta = await db.Rutas.Include(r => r.Paradas).FirstOrDefaultAsync(r => r.Id == rutaId, ct);

        if (ruta is null)
            return null;

        if (!EstadosParaRendir.Contains(ruta.Estado))
            throw new ConflictoDepositoException("Solo se rinde una hoja de ruta que ya salió.");

        var rendicion = await db.Rendiciones.FirstOrDefaultAsync(r => r.RutaId == rutaId, ct);

        if (rendicion?.Confirmada == true)
            throw new ConflictoDepositoException("La rendición de esta hoja de ruta ya está confirmada.");

        var envioIds = ruta.Paradas.Select(p => p.EnvioId).ToList();
        var envios = await db.Envios
            .Include(e => e.IntentosEntrega)
            .Include(e => e.Bultos)
            .Include(e => e.Devolucion)
            .Where(e => envioIds.Contains(e.Id))
            .ToListAsync(ct);

        var porEnvio = envios.ToDictionary(
            e => e.Id,
            e => EstadosCerrados.Contains(e.Estado)
                ? []
                : e.Bultos.Where(b => b.Estado is EstadoBulto.Pendiente or EstadoBulto.NoEntregado).Select(b => b.Codigo.ToUpperInvariant()).ToList());

        var esperados = porEnvio.Values.SelectMany(c => c).ToHashSet();
        var faltantes = esperados.Where(c => !recibidos.Contains(c)).Order().ToList();
        var sobrantes = recibidos.Where(c => !esperados.Contains(c)).Order().ToList();
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var cambios = new List<EnvioRendidoDto>();

        foreach (var envio in envios.OrderBy(e => e.Id))
        {
            var codigos = porEnvio[envio.Id];

            if (codigos.Count == 0 || codigos.Any(c => !recibidos.Contains(c)))
                continue;

            var anterior = envio.Estado;
            var regla = await db.ReglaDelEnvioAsync(envio, ct);

            try
            {
                switch (envio.Estado)
                {
                    case EstadoEnvio.EnTransito:
                        envio.CambiarEstado(EstadoEnvio.EnDeposito, "Deposito",
                            $"Volvió al depósito sin intento de entrega (rendición de \"{ruta.Nombre}\").", responsableId, regla, ahora);
                        break;

                    case EstadoEnvio.NoEntregado or EstadoEnvio.EntregadoParcialmente
                        when envio.EstadosSiguientes(regla).Contains(EstadoEnvio.Reprogramado):
                        envio.CambiarEstado(EstadoEnvio.Reprogramado, "Deposito",
                            $"Volvió al depósito en la rendición de \"{ruta.Nombre}\": sale en otra hoja de ruta.", responsableId, regla, ahora);
                        break;

                    case EstadoEnvio.NoEntregado or EstadoEnvio.EntregadoParcialmente:
                        envio.CambiarEstado(EstadoEnvio.EnDevolucion, "Deposito",
                            $"Se agotaron los intentos de entrega: vuelve al comercio (rendición de \"{ruta.Nombre}\").", responsableId, regla, ahora);
                        break;
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new ConflictoDepositoException($"{envio.CodigoSeguimiento}: {ex.Message}");
            }

            if (envio.Estado != anterior)
                cambios.Add(new EnvioRendidoDto(envio.Id, envio.CodigoSeguimiento, anterior.ToString(), envio.Estado.ToString()));
        }

        if (rendicion is null)
        {
            rendicion = new Rendicion { RutaId = rutaId };
            db.Rendiciones.Add(rendicion);
        }

        rendicion.FechaHora = ahora;
        rendicion.ConfirmadaEn = ahora;
        rendicion.ConfirmadaPor = responsableId;
        rendicion.Observaciones = observaciones;
        rendicion.BultosRecibidos = recibidos.Order().ToList();
        rendicion.Faltantes = faltantes;
        rendicion.Sobrantes = sobrantes;
        rendicion.CantidadEntregados = envios.Count(e => e.Estado == EstadoEnvio.Entregado);
        rendicion.CantidadNoEntregados = envios.Count - rendicion.CantidadEntregados;

        ruta.Cerrar(ahora);

        await GuardarAsync(ct);
        await transaccion.CommitAsync(ct);

        var resultado = await ObtenerAsync(rutaId, ct);
        return resultado! with { Envios = cambios };
    }

    private async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictoDepositoException("Otra persona cambió la ruta o un envío al mismo tiempo. Volvé a cargarla.");
        }
        catch (DbUpdateException)
        {
            throw new ConflictoDepositoException("La rendición de esta hoja de ruta ya se registró al mismo tiempo. Volvé a cargarla.");
        }
    }

    private static string? Observaciones(string? valor)
    {
        var observaciones = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        if (observaciones?.Length > 500)
            throw new ArgumentException("Las observaciones pueden tener hasta 500 caracteres.");

        return observaciones;
    }

    private static HashSet<string> Normalizar(IReadOnlyList<string>? codigos)
    {
        var resultado = (codigos ?? [])
            .Select(c => c?.Trim().ToUpperInvariant() ?? "")
            .Where(c => c.Length > 0)
            .ToHashSet();

        if (resultado.Count > 500 || resultado.Any(c => c.Length > 40))
            throw new ArgumentException("Hay demasiados códigos o alguno es demasiado largo.");

        return resultado;
    }

    private static bool ADevolver(BultoDeRuta b) =>
        !EstadosCerrados.Contains(b.EstadoEnvio) && b.EstadoBulto is EstadoBulto.Pendiente or EstadoBulto.NoEntregado;

    private async Task<List<BultoDeRuta>> BultosDeLaRutaAsync(int rutaId, CancellationToken ct) =>
        await (
                from p in db.Paradas.AsNoTracking()
                join b in db.Bultos on p.EnvioId equals b.EnvioId
                join e in db.Envios on p.EnvioId equals e.Id
                join d in db.Destinatarios on e.DestinatarioId equals d.Id
                where p.RutaId == rutaId
                orderby p.Orden, b.Id
                select new BultoDeRuta(b.Codigo.ToUpper(), e.Id, e.CodigoSeguimiento, d.Nombre + " " + d.Apellido, e.Estado, b.Estado))
            .ToListAsync(ct);

    private sealed record BultoDeRuta(
        string Codigo, int EnvioId, string CodigoSeguimiento, string Destinatario, EstadoEnvio EstadoEnvio, EstadoBulto EstadoBulto);
}
