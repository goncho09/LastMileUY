using LastMileUY.API.Infrastructure.Correlacion;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public class OutboxInterceptor(ContextoCorrelacion correlacion, SenalOutbox senal) : SaveChangesInterceptor
{
    private bool agregoMensajes;

    private readonly HashSet<EventoEnvio> conMensaje = new(ReferenceEqualityComparer.Instance);

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AgregarMensajes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AgregarMensajes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AvisarSiCorresponde();
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        AvisarSiCorresponde();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void AgregarMensajes(DbContext? db)
    {
        if (db is null)
            return;

        db.ChangeTracker.DetectChanges();

        var ahora = DateTime.UtcNow;
        var mensajes = new List<MensajeOutbox>();

        var origenPorEnvio = new Dictionary<Envio, EventoEnvio>(ReferenceEqualityComparer.Instance);

        foreach (var entrada in db.ChangeTracker.Entries<Envio>().ToList())
        {
            if (entrada.State == EntityState.Deleted)
                continue;

            var envio = entrada.Entity;
            var esNuevo = entrada.State == EntityState.Added;

            string? anterior = esNuevo
                ? null
                : entrada.OriginalValues.GetValue<EstadoEnvio>(nameof(Envio.Estado)).ToString();

            var eventosNuevos = envio.Eventos
                .Where(evento => db.Entry(evento).State == EntityState.Added)
                .ToList();

            if (eventosNuevos.Count > 0)
                origenPorEnvio[envio] = eventosNuevos[0];

            foreach (var evento in eventosNuevos)
            {
                if (!conMensaje.Add(evento))
                {
                    anterior = evento.Tipo;
                    continue;
                }

                mensajes.Add(new MensajeOutbox
                {
                    Tipo = esNuevo && anterior is null ? TiposEvento.EnvioAdmitido : TiposEvento.EnvioEstadoCambiado,
                    Envio = envio,
                    OperadorId = envio.OperadorId,
                    ComercioId = envio.ComercioId,
                    CodigoSeguimiento = envio.CodigoSeguimiento,
                    Estado = evento.Tipo,
                    EstadoAnterior = anterior,
                    Origen = evento.Origen,
                    EsPublico = evento.EsPublico,
                    OcurridoEn = evento.FechaHora,
                    CorrelacionId = correlacion.Id,
                    FechaCreacion = ahora.AddTicks(mensajes.Count * 10)
                });

                anterior = evento.Tipo;
            }
        }

        AgregarMensajesDeBultos(db, mensajes, origenPorEnvio, ahora);

        db.AddRange(mensajes);
        agregoMensajes |= mensajes.Count > 0;
    }

    private void AgregarMensajesDeBultos(
        DbContext db,
        List<MensajeOutbox> mensajes,
        Dictionary<Envio, EventoEnvio> origenPorEnvio,
        DateTime ahora)
    {
        var bultos = db.ChangeTracker.Entries<Bulto>()
            .Where(b => b.State != EntityState.Deleted && b.Entity.CambiosSinPublicar.Count > 0)
            .Select(b => b.Entity)
            .ToList();

        if (bultos.Count == 0)
            return;

        var motivos = bultos
            .Select(b => b.CambiosSinPublicar[^1].MotivoNoEntregaId)
            .OfType<int>()
            .Distinct()
            .ToList();
        var nombres = motivos.Count == 0 ? new Dictionary<int, string>() : NombresDeMotivos(db, motivos);

        foreach (var bulto in bultos.OrderBy(b => b.EnvioId).ThenBy(b => b.Id))
        {
            var envio = db.Set<Envio>().Local.FirstOrDefault(e => e.Id == bulto.EnvioId)
                ?? db.Set<Envio>().FirstOrDefault(e => e.Id == bulto.EnvioId)
                ?? throw new InvalidOperationException(
                    $"No se encontró el envío del bulto {bulto.Codigo} para publicar su cambio.");

            var cambios = bulto.CambiosSinPublicar;
            var ultimo = cambios[^1];
            origenPorEnvio.TryGetValue(envio, out var origen);

            mensajes.Add(new MensajeOutbox
            {
                Tipo = TiposEvento.BultoEstadoCambiado,
                Envio = envio,
                OperadorId = envio.OperadorId,
                ComercioId = envio.ComercioId,
                CodigoSeguimiento = envio.CodigoSeguimiento,
                CodigoBulto = bulto.Codigo,
                Estado = ultimo.Nuevo.ToString(),
                EstadoAnterior = cambios[0].Anterior.ToString(),
                MotivoNoEntrega = ultimo.MotivoNoEntregaId is { } motivo ? nombres.GetValueOrDefault(motivo) : null,
                Origen = origen?.Origen ?? "Sistema",
                EsPublico = origen?.EsPublico ?? true,
                OcurridoEn = origen?.FechaHora ?? ahora,
                CorrelacionId = correlacion.Id,
                FechaCreacion = ahora.AddTicks(mensajes.Count * 10)
            });

            bulto.MarcarCambiosPublicados();
        }
    }

    private static Dictionary<int, string> NombresDeMotivos(DbContext db, IReadOnlyCollection<int> ids)
    {
        var nombres = db.Set<MotivoNoEntrega>().Local
            .Where(m => ids.Contains(m.Id))
            .ToDictionary(m => m.Id, m => m.Nombre);

        var faltan = ids.Where(id => !nombres.ContainsKey(id)).ToList();

        if (faltan.Count > 0)
        {
            foreach (var motivo in db.Set<MotivoNoEntrega>().AsNoTracking()
                         .Where(m => faltan.Contains(m.Id))
                         .Select(m => new { m.Id, m.Nombre })
                         .ToList())
            {
                nombres[motivo.Id] = motivo.Nombre;
            }
        }

        return nombres;
    }

    private void AvisarSiCorresponde()
    {
        if (!agregoMensajes)
            return;

        agregoMensajes = false;
        senal.Avisar();
    }
}

public class SenalOutbox
{
    private readonly SemaphoreSlim senal = new(0, 1);

    public void Avisar()
    {
        try
        {
            senal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> EsperarAsync(TimeSpan maximo, CancellationToken cancellationToken) =>
        senal.WaitAsync(maximo, cancellationToken);
}
