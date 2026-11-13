namespace LastMileUY.Contracts.Destinatarios;

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
