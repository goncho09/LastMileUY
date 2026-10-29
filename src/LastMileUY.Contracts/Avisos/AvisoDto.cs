namespace LastMileUY.Contracts.Avisos;

// Un aviso mandado (o por mandar) al sistema del comercio (GET /api/avisos).
// Estado: Pendiente (se está intentando), Entregado o Fallido (se agotaron los intentos).
public record AvisoResumenDto(
    int Id,
    string TipoEvento,
    string CodigoSeguimiento,
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
