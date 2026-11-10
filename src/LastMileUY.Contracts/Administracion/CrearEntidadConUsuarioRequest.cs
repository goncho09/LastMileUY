namespace LastMileUY.Contracts.Administracion;

public sealed record UsuarioInicialRequest(
    string Nombre, string Apellido, string Email, string Password);

public sealed record CrearOperadorConUsuarioRequest(
    string Nombre, string? ColorPrimario, string? LogoUrl,
    UsuarioInicialRequest Usuario);

public sealed record CrearComercioConUsuarioRequest(
    string Nombre, string? RazonSocial, string? RUT,
    string? Email, string? Telefono, UsuarioInicialRequest Usuario);
