namespace LastMileUY.Contracts.Sincronizacion;

public record OperacionSincronizadaDto(
    Guid IdOperacion,
    string Tipo,
    string Resultado,
    string Mensaje,
    int RutaId,
    int EnvioId,
    string? CodigoSeguimiento,
    string Repartidor,
    DateTime FechaHoraDispositivo,
    DateTime FechaRecepcion,
    int? IntentoEntregaId,
    bool TieneFirma,
    bool TieneFoto,
    int? VersionReglasEntregaParcial = null);

