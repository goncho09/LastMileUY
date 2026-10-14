namespace LastMileUY.Contracts.Destinatarios;

// Un destinatario con sus direcciones activas (GET /api/destinatarios)
public record DestinatarioDto(
    int Id,
    int ComercioId,
    string Nombre,
    string Apellido,
    string? Email,
    string? Telefono,
    List<DireccionDto> Direcciones);
