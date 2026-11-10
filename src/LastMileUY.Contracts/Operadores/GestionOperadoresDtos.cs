namespace LastMileUY.Contracts.Operadores;

public sealed record OperadorAdminDto(
    int Id,
    string Nombre,
    bool Activo,
    DateTime FechaCreacion,
    string? ColorPrimario,
    string? LogoUrl);

public sealed record GuardarOperadorAdminRequest(
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl);