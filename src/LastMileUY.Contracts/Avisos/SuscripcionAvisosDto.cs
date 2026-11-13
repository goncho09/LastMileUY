namespace LastMileUY.Contracts.Avisos;

public record SuscripcionAvisosDto(
    int Id,
    string TipoEvento,
    string UrlDestino,
    bool Activa,
    DateTime FechaCreacion,
    int Version = VersionesAvisos.PorDefecto);

public class CrearSuscripcionAvisosRequest
{
    public string TipoEvento { get; set; } = string.Empty;

    public string UrlDestino { get; set; } = string.Empty;

    public int Version { get; set; } = VersionesAvisos.PorDefecto;
}

public record CambiarSuscripcionAvisosRequest(bool Activa);

public record SuscripcionAvisosCreadaDto(
    int Id,
    string TipoEvento,
    string UrlDestino,
    bool Activa,
    DateTime FechaCreacion,
    string ClaveFirma,
    int Version = VersionesAvisos.PorDefecto);