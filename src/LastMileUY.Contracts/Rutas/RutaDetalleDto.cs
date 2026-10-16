using LastMileUY.Contracts.Envios;

namespace LastMileUY.Contracts.Rutas;

// La hoja de ruta completa (GET /api/rutas/{id}): la ve el Backoffice y la descarga el repartidor
public record RutaDetalleDto(
    int Id,
    string Nombre,
    DateOnly Fecha,
    string Estado,
    RepartidorDto Repartidor,
    VehiculoDto Vehiculo,
    decimal PesoTotalKg,
    decimal VolumenTotalM3,
    IReadOnlyList<ParadaDto> Paradas);

public record RepartidorDto(
    int Id,
    string Nombre);

public record VehiculoDto(
    int Id,
    string Matricula,
    string Tipo,
    decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3);

// Una parada: el envío que se entrega ahí, en el orden de la ruta
public record ParadaDto(
    int Orden,
    int EnvioId,
    string CodigoSeguimiento,
    string EstadoEnvio,
    DestinatarioDto Destinatario,
    DireccionDto Direccion,
    FranjaHorariaDto? Franja,
    int CantidadBultos,
    decimal PesoKg,
    decimal VolumenM3);

public record FranjaHorariaDto(
    string Nombre,
    TimeOnly HoraInicio,
    TimeOnly HoraFin);
