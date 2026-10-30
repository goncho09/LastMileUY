namespace LastMileUY.Contracts.Mensajeria;

public record MensajeFallidoResponse(
    Guid Id,
    string MensajeId,
    string? CorrelacionId,
    string? Tipo,
    string? UltimoError,
    int Intentos,
    DateTime FechaFallo,
    string Estado,
    DateTime? FechaResolucion
);