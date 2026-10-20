namespace LastMileUY.Contracts.Entregas;

public record RegistrarIntentoFallidoRequest(
    int MotivoNoEntregaId,
    string? Observaciones
);