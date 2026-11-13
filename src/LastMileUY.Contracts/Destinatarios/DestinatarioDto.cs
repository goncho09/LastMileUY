namespace LastMileUY.Contracts.Destinatarios;

public record DestinatarioDto(
    int Id,
    int ComercioId,
    string Nombre,
    string Apellido,
    string? Email,
    string? Telefono,
    List<DireccionDto> Direcciones);
