namespace LastMileUY.Contracts.Envios;

public record ActividadEnvioDto(
    int Id,
    int EnvioId,
    string CodigoSeguimiento,
    string Estado,
    string? Descripcion,
    string Origen,
    DateTime FechaHora);