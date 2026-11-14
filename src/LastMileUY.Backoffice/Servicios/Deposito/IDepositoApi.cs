using LastMileUY.Contracts.Deposito;

namespace LastMileUY.Backoffice.Servicios;

public interface IDepositoApi
{
    Task<IReadOnlyList<EnvioPorRecibirDto>> PorRecibirAsync(CancellationToken ct = default);

    Task<EnvioParaRecibirDto?> BuscarAsync(string codigo, CancellationToken ct = default);

    Task<RecepcionDto> RecibirAsync(RegistrarRecepcionRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<RecepcionDto>> DiscrepanciasAsync(bool soloPendientes, CancellationToken ct = default);

    Task ResolverAsync(int recepcionId, string resolucion, CancellationToken ct = default);
}
