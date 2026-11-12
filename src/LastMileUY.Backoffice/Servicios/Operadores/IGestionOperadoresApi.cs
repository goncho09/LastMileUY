using LastMileUY.Contracts.Administracion;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public interface IGestionOperadoresApi
{
    Task<List<OperadorAdminDto>> ListarAsync();
    Task<OperadorAdminDto?> ObtenerAsync(int id);
    Task<OperadorAdminDto> CrearAsync(CrearOperadorConUsuarioRequest request);
    Task EditarAsync(int id, GuardarOperadorAdminRequest request);
}