using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.Backoffice.Servicios;

public interface IEnviosApi
{
    Task<PaginaDto<EnvioResumenDto>> ListarAsync(FiltroEnvios filtro, CancellationToken ct = default);

    Task<EnvioDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<EnvioDetalleDto?> CambiarEstadoAsync(int id, string estado, string? descripcion, CancellationToken ct = default);

    Task<List<ActividadEnvioDto>> ObtenerActividadRecienteAsync(
        CancellationToken ct = default);
}

public class OperacionRechazadaException(string mensaje) : Exception(mensaje);
