namespace LastMileUY.Contracts.Rutas;

public record RutaResumenDto(
    int Id,
    string Nombre,
    DateOnly Fecha,
    string Estado,
    string Repartidor,
    string Vehiculo,
    int CantidadParadas);
