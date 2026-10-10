namespace LastMileUY.API.Infrastructure.Multitenancy;

// Operador fijado a mano: para el worker (cada mensaje trae su operador),
// las pruebas y las herramientas de EF (sin operador).
public class ContextoOperadorFijo(int? operadorId) : IContextoOperador
{
    public int? OperadorId { get; } = operadorId;
}
