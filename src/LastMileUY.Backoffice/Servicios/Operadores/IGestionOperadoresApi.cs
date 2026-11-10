using LastMileUY.Contracts.Operadores;

public interface IGestionOperadoresApi
{
    Task<List<OperadorAdminDto>> ListarAsync();
    Task<OperadorAdminDto?> ObtenerAsync(int id);
    Task<OperadorAdminDto> CrearAsync(GuardarOperadorAdminRequest request);
    Task EditarAsync(int id, GuardarOperadorAdminRequest request);
}