namespace LastMileUY.Contracts.Usuarios;

public sealed record UsuarioDto(
    string Id,
    string Email,
    string Nombre,
    string Apellido,
    string Rol,
    int? OperadorId,
    int? ComercioId,
    bool Activo,
    DateTime FechaCreacion
);