namespace LastMileUY.Contracts.Usuarios;

public sealed record CrearUsuarioRequest(
    string Email,
    string Nombre,
    string Apellido,
    string Password,
    string Rol,
    int? OperadorId,
    int? ComercioId
);