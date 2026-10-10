namespace LastMileUY.API.Infrastructure.Multitenancy;

// Dice qué operador está usando el sistema en este pedido (resolución del inquilino, ADR-01).
// Si es null no hay operador, y no se ve ningún dato de operador.
public interface IContextoOperador
{
    int? OperadorId { get; }
}
