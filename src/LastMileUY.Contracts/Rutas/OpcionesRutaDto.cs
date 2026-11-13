namespace LastMileUY.Contracts.Rutas;

public record OpcionesRutaDto(
    IReadOnlyList<RepartidorDto> Repartidores,
    IReadOnlyList<VehiculoDto> Vehiculos,
    IReadOnlyList<EnvioDisponibleDto> Envios);

public record EnvioDisponibleDto(
    int Id,
    string CodigoSeguimiento,
    string Estado,
    string Destinatario,
    string Direccion,
    FranjaHorariaDto? Franja,
    decimal PesoKg,
    decimal VolumenM3,
    bool TieneUbicacion = false);
