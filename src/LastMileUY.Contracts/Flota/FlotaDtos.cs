namespace LastMileUY.Contracts.Flota;

public record PosicionRequest(decimal Latitud, decimal Longitud, DateTime FechaHora);

public record RegistrarPosicionesRequest(IReadOnlyList<PosicionRequest> Posiciones);

public record RegistrarPosicionesResponse(int Guardadas, int Descartadas);

public record VehiculoEnRutaDto(
    int RutaId,
    string Ruta,
    string Repartidor,
    string Vehiculo,
    string Estado,
    int Paradas,
    int Pendientes,
    decimal? Latitud,
    decimal? Longitud,
    DateTime? UltimaPosicion);

public record EnvioEnRiesgoDto(
    int EnvioId,
    string CodigoSeguimiento,
    int RutaId,
    string Ruta,
    string Destinatario,
    string Motivo,
    DateTime Limite,
    bool Vencido);

public record FlotaDto(
    DateTime Generado,
    IReadOnlyList<VehiculoEnRutaDto> Rutas,
    IReadOnlyList<EnvioEnRiesgoDto> EnRiesgo);
