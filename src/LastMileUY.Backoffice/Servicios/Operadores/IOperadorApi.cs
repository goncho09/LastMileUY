using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

public interface IOperadorApi
{
    Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReglaEntregaParcialDto>> ListarReglasEntregaParcialAsync(CancellationToken ct = default);

    Task<ReglaEntregaParcialDto> PublicarReglaEntregaParcialAsync(
        PublicarReglaEntregaParcialRequest request, CancellationToken ct = default);

    Task GuardarDepositoAsync(Coordenadas? deposito, CancellationToken ct = default);
}
