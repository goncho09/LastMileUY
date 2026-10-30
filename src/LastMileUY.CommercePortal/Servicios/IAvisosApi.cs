using LastMileUY.Contracts.Avisos;

namespace LastMileUY.CommercePortal.Servicios;

public interface IAvisosApi
{
    Task<List<SuscripcionAvisosDto>> ListarSuscripcionesAsync(
        CancellationToken ct = default);

    Task<ResultadoCreacionSuscripcion> CrearSuscripcionAsync(
        CrearSuscripcionAvisosRequest request,
        CancellationToken ct = default);

    Task<ResultadoSuscripcion> CambiarActivaAsync(
        int id,
        bool activa,
        CancellationToken ct = default);
    
    Task<ResultadoCreacionSuscripcion> RegenerarClaveAsync(
        int id,
        CancellationToken ct = default);
}

// Resultado de crear una suscripción.
// Incluye la clave secreta que se muestra una sola vez.
public record ResultadoCreacionSuscripcion(
    SuscripcionAvisosCreadaDto? Suscripcion,
    string? Error)
{
    public bool Exito => Suscripcion is not null;
}

// Resultado de modificar una suscripción.
// No contiene la clave secreta.
public record ResultadoSuscripcion(
    SuscripcionAvisosDto? Suscripcion,
    string? Error)
{
    public bool Exito => Suscripcion is not null;
}