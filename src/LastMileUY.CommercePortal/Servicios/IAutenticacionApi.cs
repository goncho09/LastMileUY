using LastMileUY.Contracts.Auth;

namespace LastMileUY.CommercePortal.Servicios;

// Login del usuario de comercio contra la API y manejo del operador con el que trabaja.
// Recibe la cookie de la API a mano porque se usa desde páginas sin interactividad (con HttpContext).
public interface IAutenticacionApi
{
    Task<ResultadoLogin> IniciarSesionAsync(string email, string password, int? operadorId, CancellationToken ct = default);

    Task<List<OperadorDisponibleDto>> ListarOperadoresAsync(string cookieApi, CancellationToken ct = default);

    Task<ResultadoLogin> CambiarOperadorAsync(string cookieApi, int operadorId, CancellationToken ct = default);

    Task CerrarSesionAsync(string cookieApi, CancellationToken ct = default);
}

public enum EstadoLogin
{
    Ok,
    CredencialesInvalidas,
    ElegirOperador,
    Rechazado
}

public record ResultadoLogin(
    EstadoLogin Estado,
    SesionApi? Sesion = null,
    string? Mensaje = null,
    List<OperadorDisponibleDto>? Operadores = null);

// Quién entró, con qué comercio y operador, y la cookie que le dio la API
public record SesionApi(
    string Nombre,
    string Email,
    int ComercioId,
    int OperadorId,
    string? OperadorNombre,
    string CookieApi);
