namespace LastMileUY.Contracts.Avisos;

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
    string? UltimoError,
    string? CodigoBulto = null,
    string? MotivoNoEntrega = null,
    int Version = VersionesAvisos.PorDefecto);

public record IntentoAvisoDto(
    int NumeroIntento,
    DateTime Fecha,
    bool Exitoso,
    int? CodigoHttp,
    string? Error,
    int DuracionMs);

public record AvisoDetalleDto(AvisoResumenDto Aviso, IReadOnlyList<IntentoAvisoDto> Intentos);
