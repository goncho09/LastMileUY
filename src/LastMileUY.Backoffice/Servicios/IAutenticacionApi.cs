namespace LastMileUY.Backoffice.Servicios;

// Inicia sesión contra la API. Devuelve null si el email o la contraseña no son válidos.
public interface IAutenticacionApi
{
    Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default);
}

// Quién inició sesión y la cookie que la API le dio (se reenvía en cada llamada).
// CookieApi es null con los datos de prueba, donde no hay API.
public record SesionIniciada(
    string Nombre,
    string Email,
    int? OperadorId,
    string? CookieApi);
