namespace LastMileUY.Contracts.Rutas;

// Lo que se puede elegir al armar una ruta (GET /api/rutas/opciones):
// repartidores y vehículos activos, y los envíos que todavía no están en ninguna ruta.
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
    decimal VolumenM3);
