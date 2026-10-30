namespace LastMileUY.Contracts.Mensajeria;

public record MensajeFallidoDetalleResponse(
    Guid Id,
    string MensajeId,
    string? CorrelacionId,
    string? Tipo,
    string Contenido,
    string? UltimoError,
    int Intentos,
    DateTime FechaFallo,
    string Estado,
    DateTime? FechaSolicitudReproceso,
    string? SolicitadoPorId,
    DateTime? FechaResolucion
);