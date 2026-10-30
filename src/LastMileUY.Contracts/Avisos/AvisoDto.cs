namespace LastMileUY.Contracts.Avisos;

// Un aviso mandado (o por mandar) al sistema del comercio (GET /api/avisos).
// Estado: el del aviso, no el del paquete. Pendiente (se está intentando), Entregado (le llegó al
// sistema del comercio) o Fallido (se agotaron los intentos).
// EstadoEnvio / EstadoEnvioAnterior: lo que el aviso le cuenta (el envío pasó de uno a otro);
// el anterior es null cuando el envío se acaba de crear.
public record AvisoResumenDto(
    int Id,
    string TipoEvento,
    string CodigoSeguimiento,
    string? EstadoEnvio,
    string? EstadoEnvioAnterior,
    string UrlDestino,
    string Estado,
    int CantidadIntentos,
    DateTime FechaCreacion,
    DateTime? FechaUltimoIntento,
    DateTime? FechaProximoIntento,
    string? UltimoError);

// Un intento de entrega del aviso (historial)
public record IntentoAvisoDto(
    int NumeroIntento,
    DateTime Fecha,
    bool Exitoso,
    int? CodigoHttp,
    string? Error,
    int DuracionMs);

// El aviso con todos sus intentos, del primero al último (GET /api/avisos/{id})
public record AvisoDetalleDto(AvisoResumenDto Aviso, IReadOnlyList<IntentoAvisoDto> Intentos);
