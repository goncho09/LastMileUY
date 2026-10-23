using LastMileUY.API.Infrastructure.Correlacion;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Al guardar cambios, cada evento nuevo del historial de un envío (alta o cambio de estado, ver
// Envio.CambiarEstado) suma un MensajeOutbox al mismo guardado. Así el evento queda en la misma
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

        db.AddRange(mensajes);
        agregoMensajes |= mensajes.Count > 0;
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
