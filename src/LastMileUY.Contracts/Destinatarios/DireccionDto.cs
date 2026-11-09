namespace LastMileUY.Contracts.Destinatarios;

// Una dirección activa de un destinatario. Latitud y Longitud: dónde queda en el mapa, si se cargó
public record DireccionDto(
    int Id,
    string Calle,
    string Numero,
    string? Apartamento,
    string Ciudad,
    string Departamento,
    string? CodigoPostal,
    string? Referencia,
    decimal? Latitud = null,
    decimal? Longitud = null);
