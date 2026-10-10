namespace LastMileUY.Contracts.Envios;

// Una fila del listado de envíos (GET /api/envios)
public record EnvioResumenDto(
    int Id,
    string CodigoSeguimiento,
    string? ReferenciaExterna,
    string Comercio,
    string Destinatario,
    string Ciudad,
    string Departamento,
    string Estado,
    string Modalidad,
    int CantidadBultos,
    DateTime FechaCreacion);
