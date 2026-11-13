using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public interface IAutenticacionApi
{
    Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default);
}

public record SesionIniciada(
    string Nombre,
    string Email,
    int? OperadorId,
    string? CookieApi,
    OperadorActualDto? Operador = null,
    IReadOnlyList<string>? Roles = null);
