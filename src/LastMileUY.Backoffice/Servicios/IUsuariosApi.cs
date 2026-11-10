using LastMileUY.Contracts.Usuarios;

namespace LastMileUY.Backoffice.Servicios;

public interface IUsuariosApi
{
    Task<IReadOnlyList<UsuarioDto>> ListarAsync(
        CancellationToken ct = default);

    Task<UsuarioDto> CrearAsync(
        CrearUsuarioRequest request,
        CancellationToken ct = default);

    Task CambiarEstadoAsync(
        string usuarioId,
        bool activo,
        CancellationToken ct = default);
}