using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

// El operador del usuario logueado (GET /api/operador): identidad visual y reglas operativas.
// Y sus reglas de entrega parcial, versionadas (/api/operador/reglas-entrega-parcial).
public interface IOperadorApi
{
    Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default);

    // Todas las versiones, la más nueva primero
    Task<IReadOnlyList<ReglaEntregaParcialDto>> ListarReglasEntregaParcialAsync(CancellationToken ct = default);

    // OperacionRechazadaException con el motivo si la API no la acepta
    Task<ReglaEntregaParcialDto> PublicarReglaEntregaParcialAsync(
        PublicarReglaEntregaParcialRequest request, CancellationToken ct = default);

    // De dónde salen las rutas (PUT /api/operador/deposito); null lo borra.
    // OperacionRechazadaException con el motivo si la API no lo acepta
    Task GuardarDepositoAsync(Coordenadas? deposito, CancellationToken ct = default);
}
