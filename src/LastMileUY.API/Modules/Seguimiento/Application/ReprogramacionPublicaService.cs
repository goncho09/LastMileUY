using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.Contracts.Seguimiento;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Seguimiento.Application;

public static class PoliticaReprogramacion
{
    public const int DiasMaximos = 14;

    public static readonly EstadoEnvio[] EstadosReprogramables =
        [EstadoEnvio.Admitido, EstadoEnvio.EnDeposito, EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado];

    public static (string? Motivo, DateOnly Minima, DateOnly Maxima) Evaluar(
        EstadoEnvio estado, int intentosFallidos, ReglaOperativa? regla, bool yaSolicito, DateOnly hoy)
    {
        var diasMinimos = Math.Max(1, (int)Math.Ceiling((regla?.HorasEntreIntentos ?? 24) / 24.0));
        var minima = hoy.AddDays(diasMinimos);
        var maxima = hoy.AddDays(Math.Max(DiasMaximos, diasMinimos));

        string? motivo = estado switch
        {
            EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito =>
                "Tu envío ya está en camino: por ahora no se puede cambiar la fecha.",
            _ when !EstadosReprogramables.Contains(estado) =>
                "Este envío ya no se puede reprogramar.",
            _ when regla is not null && intentosFallidos >= regla.MaximoIntentosEntrega =>
                "Se agotaron los intentos de entrega: el envío vuelve al comercio.",
            _ when yaSolicito =>
                "Ya pediste reprogramar esta entrega. Si necesitás otro cambio, contactá al operador.",
            _ => null
        };

        return (motivo, minima, maxima);
    }
}

public class ReprogramacionPublicaService(FabricaContextoOperador fabrica, TimeProvider reloj)
{
    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private DateOnly Hoy() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), HoraUruguay).DateTime);

    public async Task<ReprogramacionPublicaDto> OpcionesAsync(int operadorId, int envioId, CancellationToken ct = default)
    {
        await using var db = fabrica.Crear(operadorId);

        var envio = await db.Envios.AsNoTracking().Include(e => e.IntentosEntrega).FirstAsync(e => e.Id == envioId, ct);
        return (await ArmarAsync(db, envio, ct)).Opciones;
    }

    public async Task<string?> SolicitarAsync(
        int operadorId, int envioId, SolicitarReprogramacionRequest request, CancellationToken ct = default)
    {
        await using var db = fabrica.Crear(operadorId);
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var envio = await db.Envios.Include(e => e.IntentosEntrega).FirstAsync(e => e.Id == envioId, ct);
        var (opciones, franjas, regla) = await ArmarAsync(db, envio, ct);

        if (!opciones.Permitida)
            return opciones.Motivo;

        if (request.Fecha < opciones.FechaMinima || request.Fecha > opciones.FechaMaxima)
            return $"Elegí una fecha entre el {opciones.FechaMinima:dd/MM/yyyy} y el {opciones.FechaMaxima:dd/MM/yyyy}.";

        FranjaHoraria? franja = null;

        if (!string.IsNullOrWhiteSpace(request.Franja))
        {
            franja = franjas.FirstOrDefault(f => string.Equals(f.Nombre, request.Franja.Trim(), StringComparison.OrdinalIgnoreCase));

            if (franja is null)
                return "Esa franja horaria no está disponible para tu dirección.";
        }

        var comentario = string.IsNullOrWhiteSpace(request.Comentario) ? null : request.Comentario.Trim();

        if (comentario?.Length > 300)
            return "El comentario puede tener hasta 300 caracteres.";

        db.SolicitudesReprogramacion.Add(new SolicitudReprogramacion
        {
            OperadorId = operadorId,
            EnvioId = envio.Id,
            FechaSolicitada = request.Fecha,
            FranjaHorariaId = franja?.Id,
            Comentario = comentario,
            FechaCreacion = reloj.GetUtcNow().UtcDateTime
        });

        envio.FechaEntregaSolicitada = request.Fecha;

        if (franja is not null)
            envio.FranjaHorariaId = franja.Id;

        var descripcion = $"El destinatario pidió la entrega para el {request.Fecha:dd/MM/yyyy}"
            + (franja is null ? "." : $", de {franja.HoraInicio:HH\\:mm} a {franja.HoraFin:HH\\:mm}.")
            + (comentario is null ? "" : $" Comentario: {comentario}");

        if (envio.Estado == EstadoEnvio.NoEntregado)
            envio.CambiarEstado(EstadoEnvio.Reprogramado, "Destinatario", descripcion, null, regla);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return "El envío cambió mientras pedías la reprogramación. Volvé a cargar la página.";
        }

        await transaccion.CommitAsync(ct);
        return null;
    }

    private async Task<(ReprogramacionPublicaDto Opciones, List<FranjaHoraria> Franjas, ReglaOperativa? Regla)> ArmarAsync(
        ApplicationDbContext db, Envio envio, CancellationToken ct)
    {
        var regla = await db.ReglaDelEnvioAsync(envio, ct);
        var ultimoIntento = envio.IntentosEntrega.Count == 0 ? DateTime.MinValue : envio.IntentosEntrega.Max(i => i.FechaHora);

        var ultima = await db.SolicitudesReprogramacion.AsNoTracking()
            .Where(s => s.EnvioId == envio.Id)
            .OrderByDescending(s => s.FechaCreacion)
            .FirstOrDefaultAsync(ct);

        var franjas = envio.ZonaId is int zonaId
            ? await db.FranjasHorarias.AsNoTracking()
                .Where(f => f.ZonaId == zonaId && f.Activa)
                .OrderBy(f => f.HoraInicio)
                .ToListAsync(ct)
            : [];

        var (motivo, minima, maxima) = PoliticaReprogramacion.Evaluar(
            envio.Estado, envio.IntentosFallidos, regla, ultima is not null && ultima.FechaCreacion > ultimoIntento, Hoy());

        var franjaSolicitada = ultima?.FranjaHorariaId is int idFranja
            ? await db.FranjasHorarias.AsNoTracking().Where(f => f.Id == idFranja).Select(f => f.Nombre).FirstOrDefaultAsync(ct)
            : null;

        return (new ReprogramacionPublicaDto(
                motivo is null,
                motivo,
                minima,
                maxima,
                franjas.Select(f => new FranjaPublicaDto(f.Nombre, f.HoraInicio, f.HoraFin)).ToList(),
                ultima?.FechaSolicitada,
                franjaSolicitada),
            franjas,
            regla);
    }
}
