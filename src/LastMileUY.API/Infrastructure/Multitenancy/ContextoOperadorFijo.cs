namespace LastMileUY.API.Infrastructure.Multitenancy;

public class ContextoOperadorFijo(int? operadorId, int? comercioId = null) : IContextoOperador
{
    public int? OperadorId { get; } = operadorId;

    public int? ComercioId { get; } = comercioId;
}
