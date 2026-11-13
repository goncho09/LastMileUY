namespace LastMileUY.Contracts.Envios;

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
    DateTime FechaCreacion)
{
    public string Canal { get; init; } = "Api";
}
