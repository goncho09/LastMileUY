namespace LastMileUY.Contracts.Rutas;

// Una fila del listado de hojas de ruta (GET /api/rutas y GET /api/rutas/mias)
public record RutaResumenDto(
    int Id,
    string Nombre,
    DateOnly Fecha,
    string Estado,
    string Repartidor,
    string Vehiculo,
    int CantidadParadas);
