using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.Backoffice.Servicios;

public interface IMensajesFallidosApi
{
    Task<ResumenMensajesFallidosDto> ObtenerResumenAsync(
        CancellationToken ct = default);
    
    Task<PaginaResponse<MensajeFallidoResponse>> ListarAsync(
        FiltroMensajesFallidos filtro,
        CancellationToken ct = default);

    Task<MensajeFallidoDetalleResponse?> ObtenerDetalleAsync(
        Guid id,
        CancellationToken ct = default);
    
    Task<bool> ReintentarAsync(
        Guid id,
        CancellationToken ct = default);
}

public record ResumenMensajesFallidosDto(
    int CantidadPendientes);
    
    