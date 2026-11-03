namespace LastMileUY.Contracts.Sincronizacion;

public record ResultadoBultoRequest(
    int BultoId,
    bool Entregado,
    int? MotivoNoEntregaId = null
);