namespace LastMileUY.Contracts.Destinatarios;

// Una dirección activa de un destinatario
public record DireccionDto(
    int Id,
    string Calle,
    string Numero,
    string? Apartamento,
    string Ciudad,
    string Departamento,
    string? CodigoPostal,
    string? Referencia);
