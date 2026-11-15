namespace LastMileUY.Contracts.Envios;

public record DevolucionComercioDto(
    int EnvioId,
    string CodigoSeguimiento,
    string? ReferenciaExterna,
    string Destinatario,
    string Estado,
    string Motivo,
    DateTime FechaInicio,
    DateTime? FechaFinalizacion);
