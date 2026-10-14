using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// El operador del usuario logueado (GET /api/operador): identidad visual y reglas operativas
public interface IOperadorApi
{
    Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default);
}
