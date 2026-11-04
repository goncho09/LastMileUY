using LastMileUY.API.Infrastructure.Correlacion;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Al guardar cambios, cada evento nuevo del historial de un envío (alta o cambio de estado, ver
// Envio.CambiarEstado) y cada bulto que cambió de estado (Bulto.CambiosSinPublicar, entrega parcial)
// suman un MensajeOutbox al mismo guardado. Así el evento queda en la misma
// transacción que el cambio, sin importar desde dónde se hizo (Backoffice, portal, app del repartidor...).
// Se crea uno por pedido (con su DbContext): la correlación es la de ese pedido.
public class OutboxInterceptor(ContextoCorrelacion correlacion, SenalOutbox senal) : SaveChangesInterceptor
{
    private bool agregoMensajes;

    // Eventos que ya tienen su mensaje: si un guardado falla y se reintenta, el mensaje sigue en el
    // DbContext y no hay que agregarlo de nuevo
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

    // Guardado: el publicador no espera a su próxima vuelta
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

        // Los eventos se agregan a la colección del envío: hay que detectarlos antes de mirar
        db.ChangeTracker.DetectChanges();

        var ahora = DateTime.UtcNow;
        var mensajes = new List<MensajeOutbox>();

        // De dónde y cuándo vino el último cambio de cada envío: lo heredan los eventos de sus bultos
        var origenPorEnvio = new Dictionary<Envio, EventoEnvio>(ReferenceEqualityComparer.Instance);

        foreach (var entrada in db.ChangeTracker.Entries<Envio>().ToList())
        {
            if (entrada.State == EntityState.Deleted)
                continue;

            var envio = entrada.Entity;
            var esNuevo = entrada.State == EntityState.Added;

            // Estado antes de este guardado; si hubo más de un cambio, cada uno parte del anterior
            string? anterior = esNuevo
                ? null
                : entrada.OriginalValues.GetValue<EstadoEnvio>(nameof(Envio.Estado)).ToString();

            var eventosNuevos = envio.Eventos
                .Where(evento => db.Entry(evento).State == EntityState.Added)
                .ToList();

            if (eventosNuevos.Count > 0)
                origenPorEnvio[envio] = eventosNuevos[^1];

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
                    // Un microsegundo de diferencia (la precisión de PostgreSQL) para que se publiquen
                    // en el orden en que pasaron
                    FechaCreacion = ahora.AddTicks(mensajes.Count * 10)
                });

                anterior = evento.Tipo;
            }
        }

        AgregarMensajesDeBultos(db, mensajes, origenPorEnvio, ahora);

        db.AddRange(mensajes);
        agregoMensajes |= mensajes.Count > 0;
    }

    // Un mensaje por bulto que cambió de estado en este guardado (entrega parcial: entregado, no
    // entregado, devuelto...). Si cambió más de una vez (no entregado → pendiente → no entregado en
    // una segunda visita), sale uno solo: del primer estado al último. Van después de los del envío.
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
            // Normalmente el envío ya está cargado (los bultos cambian a través de él: entrega,
            // devolución...). Si se cambió el bulto solo, se trae el envío para los datos del evento.
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

            // Ya está en el outbox: si el guardado falla y se reintenta, no se agrega de nuevo
            bulto.MarcarCambiosPublicados();
        }
    }

    // Los nombres de los motivos de no entrega (del catálogo del operador) para el evento del bulto.
    // Primero los que ya están cargados; los demás, con una consulta (pocas filas, y el RLS del
    // operador que se está guardando ya está cargado en la conexión).
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

// Avisa al publicador que hay mensajes nuevos. Es solo para publicarlos enseguida: si el aviso se
// pierde (otra instancia de la API, un reinicio), el publicador igual revisa la tabla cada tanto.
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
            // Ya había un aviso pendiente: alcanza con uno
        }
    }

    // true si llegó un aviso antes del tiempo máximo
    public Task<bool> EsperarAsync(TimeSpan maximo, CancellationToken cancellationToken) =>
        senal.WaitAsync(maximo, cancellationToken);
}
