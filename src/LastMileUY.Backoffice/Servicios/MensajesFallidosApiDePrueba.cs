using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.Backoffice.Servicios;

public class MensajesFallidosApiDePrueba : IMensajesFallidosApi
{
    public Task<ResumenMensajesFallidosDto> ObtenerResumenAsync(
        CancellationToken ct = default)
    {
        return Task.FromResult(new ResumenMensajesFallidosDto(0));
    }

    public Task<PaginaResponse<MensajeFallidoResponse>> ListarAsync(
        FiltroMensajesFallidos filtro,
        CancellationToken ct = default)
    {
        var resultado = new PaginaResponse<MensajeFallidoResponse>(
            [],
            filtro.Pagina,
            filtro.TamanoPagina,
            0
        );

        return Task.FromResult(resultado);
    }

    public Task<MensajeFallidoDetalleResponse?> ObtenerDetalleAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return Task.FromResult<MensajeFallidoDetalleResponse?>(null);
    }
    
    public Task<bool> ReintentarAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }
}