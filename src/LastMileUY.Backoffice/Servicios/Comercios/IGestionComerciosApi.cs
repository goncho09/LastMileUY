using LastMileUY.Contracts.Comercios;

namespace LastMileUY.Backoffice.Servicios;

public interface IGestionComerciosApi
{
    Task<List<ComercioAdminDto>> ListarAsync();
    Task<ComercioAdminDto?> ObtenerAsync(int id);
    Task<ComercioAdminDto> CrearAsync(GuardarComercioAdminRequest request);
    Task EditarAsync(int id, GuardarComercioAdminRequest request);
    Task<List<VinculoOperadorComercioDto>> ListarVinculosAsync(int id);
    Task VincularAsync(int comercioId, int operadorId);
    Task DesvincularAsync(int comercioId, int operadorId);
}