namespace LastMileUY.Contracts.Mensajeria;

// Evento que la API publica en RabbitMQ cada vez que un envío entra al sistema o cambia de estado
// (ver docs/mensajeria.md). Lo consume el worker y, a partir de él, los avisos a los comercios.
// Sin datos personales ni notas internas: quien lo necesite consulta el envío con su operador.
// Version: sube solo si se quita o cambia el significado de un campo; agregar campos no la cambia.
public record EstadoEnvioCambiado(
    // Único por evento: el worker lo usa para no procesar dos veces el mismo mensaje
    Guid EventoId,
    // TiposEvento.EnvioAdmitido o TiposEvento.EnvioEstadoCambiado (también es la clave de ruteo)
    string Tipo,
    int Version,
    DateTime OcurridoEn,
    int OperadorId,
    int ComercioId,
    int EnvioId,
    string CodigoSeguimiento,
    // Nombre del estado (EstadoEnvio): "Admitido", "EnDeposito", "Entregado"...
    string Estado,
    // null en EnvioAdmitido
    string? EstadoAnterior,
    // Desde dónde se hizo: Backoffice, Repartidor, Portal, Api, Sistema...
    string Origen,
    // false: evento interno que no se muestra en el seguimiento público
    bool EsPublico,
    // El del pedido HTTP que originó el cambio (encabezado X-Correlation-Id)
    string? CorrelacionId)
{
    public const int VersionActual = 1;
}

public static class TiposEvento
{
    public const string EnvioAdmitido = "envio.admitido";
    public const string EnvioEstadoCambiado = "envio.estado_cambiado";
}
