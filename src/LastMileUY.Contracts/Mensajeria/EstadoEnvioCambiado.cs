using System.Text.Json.Serialization;

namespace LastMileUY.Contracts.Mensajeria;

// Evento que la API publica en RabbitMQ cada vez que un envío entra al sistema o cambia de estado,
// o cambia el estado de uno de sus bultos (ver docs/mensajeria.md). Lo consume el worker y, a partir
// de él, los avisos a los comercios.
// Eventos de bulto (TiposEvento.BultoEstadoCambiado): Estado y EstadoAnterior son los del bulto
// (EstadoBulto) y además llevan CodigoBulto y, si no se entregó, MotivoNoEntrega. En los eventos de
// envío esos dos campos no viajan (ni siquiera vacíos): su JSON es exactamente el de siempre.
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
    string? CorrelacionId,
    // Solo en eventos de bulto: el código del bulto que cargó el comercio (no el id interno)
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CodigoBulto = null,
    // Solo en eventos de bulto no entregado: el motivo, del catálogo del operador
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MotivoNoEntrega = null)
{
    public const int VersionActual = 1;
}

public static class TiposEvento
{
    public const string EnvioAdmitido = "envio.admitido";
    public const string EnvioEstadoCambiado = "envio.estado_cambiado";

    // Un bulto del envío cambió de estado (entrega parcial): entregado, no entregado, devuelto...
    public const string BultoEstadoCambiado = "bulto.estado_cambiado";
}
