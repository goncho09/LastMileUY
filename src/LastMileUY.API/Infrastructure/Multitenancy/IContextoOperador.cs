namespace LastMileUY.API.Infrastructure.Multitenancy;

// Dice quién está usando el sistema en este pedido (resolución del inquilino, ADR-01).
// OperadorId: primer nivel. Si es null no hay operador, y no se ve ningún dato de operador.
// ComercioId: segundo nivel. Si no es null (usuario de un comercio), además solo se ve lo de ese comercio.
public interface IContextoOperador
{
    int? OperadorId { get; }

    int? ComercioId { get; }
}
