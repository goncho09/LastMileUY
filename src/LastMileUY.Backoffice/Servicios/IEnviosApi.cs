using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.Backoffice.Servicios;

// Lo que el Backoffice necesita de la API sobre envíos.
// Hay dos implementaciones: una con datos de prueba y otra que llama a la API real.
public interface IEnviosApi
{
    Task<PaginaDto<EnvioResumenDto>> ListarAsync(FiltroEnvios filtro, CancellationToken ct = default);

    // Devuelve null si el envío no existe (o no es del operador)
    Task<EnvioDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default);
}
