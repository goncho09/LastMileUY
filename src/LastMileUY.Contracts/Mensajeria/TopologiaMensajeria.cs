namespace LastMileUY.Contracts.Mensajeria;

// Nombres que la API (publica) y el worker (consume) tienen que compartir en RabbitMQ
public static class TopologiaMensajeria
{
    // Exchange de tipo topic: la clave de ruteo es el tipo de evento (TiposEvento)
    public const string ExchangeEnvios = "lastmileuy.envios";

    // Cola del worker: recibe todos los eventos de envío ("envio.#")
    public const string ColaWorker = "lastmileuy.worker.envios";

    public const string PatronEventosEnvio = "envio.#";

    // Encabezado HTTP con el que llega (o sale) el identificador de correlación de un pedido.
    // En el mensaje viaja en la propiedad CorrelationId de AMQP y en el propio evento.
    public const string EncabezadoCorrelacion = "X-Correlation-Id";
}
