using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Un evento que el worker ya procesó (ver docs/mensajeria.md). RabbitMQ entrega "al menos una vez":
// el mismo evento puede llegar dos veces. El worker anota el evento en la misma transacción que lo que
// hace con él; si ya estaba anotado, lo descarta. La tabla la escribe el worker, no la API.
public class MensajeProcesado : IPerteneceAOperador
{
    // EventoId del contrato
    public Guid MensajeId { get; set; }

    // Qué proceso del worker lo procesó: un mismo evento lo pueden procesar varios, una vez cada uno
    public string Consumidor { get; set; } = string.Empty;

    public int OperadorId { get; set; }

    public DateTime FechaProcesado { get; set; }
}
