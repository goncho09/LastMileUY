using LastMileUY.API.Modules.Envios.Domain;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Un evento de envío esperando a publicarse en RabbitMQ (patrón outbox, ver docs/mensajeria.md).
// Se guarda en la misma transacción que el cambio del envío: o quedan los dos o ninguno, así que no
// hay cambios sin evento ni eventos de cambios que no se guardaron. PublicadorOutbox lo publica después.
// Los campos son los del contrato (EstadoEnvioCambiado), más el control de la publicación.
public class MensajeOutbox
{
    // Es el EventoId del contrato
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Tipo { get; set; } = string.Empty;

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int EnvioId { get; set; }

    // Solo para que EF guarde primero el envío nuevo y complete EnvioId con el id que le dio la base
    public Envio Envio { get; set; } = null!;

    public string CodigoSeguimiento { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public string? EstadoAnterior { get; set; }

    public string Origen { get; set; } = string.Empty;

    public bool EsPublico { get; set; }

    public DateTime OcurridoEn { get; set; }

    public string? CorrelacionId { get; set; }

    // Orden de publicación
    public DateTime FechaCreacion { get; set; }

    // null: falta publicarlo
    public DateTime? FechaPublicacion { get; set; }

    public int Intentos { get; set; }

    public string? UltimoError { get; set; }
}
