using LastMileUY.Contracts.Auth;

namespace LastMileUY.CommercePortal.Servicios;

public interface IAutenticacionApi
{
    Task<ResultadoLogin> IniciarSesionAsync(
        string email, string password, int? operadorId,
        CancellationToken ct = default);

    Task<ResultadoLogin> SeleccionarOperadorAsync(
        string tokenTemporal, int operadorId,
        CancellationToken ct = default);

    Task<List<OperadorDisponibleDto>> ListarOperadoresAsync(
        string cookieApi, CancellationToken ct = default);

    Task<ResultadoLogin> CambiarOperadorAsync(
        string cookieApi, int operadorId, CancellationToken ct = default);

    Task CerrarSesionAsync(
        string cookieApi, CancellationToken ct = default);
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
    List<OperadorDisponibleDto>? Operadores = null,
    string? TokenTemporal = null);

public record SesionApi(
    string Nombre,
    string Email,
    int ComercioId,
    int OperadorId,
    string? OperadorNombre,
    string CookieApi);


