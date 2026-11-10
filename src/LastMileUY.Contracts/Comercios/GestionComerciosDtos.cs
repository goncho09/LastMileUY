namespace LastMileUY.Contracts.Comercios;

public sealed record ComercioAdminDto(
    int Id,
    string Nombre,
    string? RazonSocial,
    string? RUT,
    string? Email,
    string? Telefono,
    bool Activo,
    DateTime FechaCreacion);

public sealed record GuardarComercioAdminRequest(
    string Nombre,
    string? RazonSocial,
    string? RUT,
    string? Email,
    string? Telefono);

public sealed record VinculoOperadorComercioDto(
    int OperadorId,
    string OperadorNombre,
    bool Activo,
    DateTime FechaVinculacion);