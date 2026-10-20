namespace LastMileUY.Contracts.Entregas;

public record MotivoNoEntregaDto(
    int Id,
    string Nombre,
    string? Descripcion
);