namespace LastMileUY.API.Infrastructure.Multitenancy;

// Operador (y comercio) fijados a mano: para el worker (cada mensaje trae su operador),
// las pruebas y las herramientas de EF (sin operador).
public class ContextoOperadorFijo(int? operadorId, int? comercioId = null) : IContextoOperador
{
    public int? OperadorId { get; } = operadorId;

    public int? ComercioId { get; } = comercioId;
}
