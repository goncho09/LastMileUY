using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Comun;

namespace LastMileUY.CommercePortal.Servicios;

// API de suscripciones, gestión de claves HMAC, historial y reenvío de avisos.
public interface IAvisosApi
{
    Task<List<SuscripcionAvisosDto>> ListarSuscripcionesAsync(
        CancellationToken ct = default);

    Task<ResultadoCreacionSuscripcion> CrearSuscripcionAsync(
        CrearSuscripcionAvisosRequest request, CancellationToken ct = default);

    Task<ResultadoSuscripcion> CambiarActivaAsync(
        int id, bool activa, CancellationToken ct = default);

    Task<ResultadoCreacionSuscripcion> RegenerarClaveAsync(
        int id, CancellationToken ct = default);

    Task<PaginaDto<AvisoResumenDto>> ListarAvisosAsync(
        int pagina, int tamanioPagina, string? estado, CancellationToken ct = default);

    Task<AvisoDetalleDto?> ObtenerAvisoAsync(int id, CancellationToken ct = default);

    Task<ResultadoReenvio> ReenviarAsync(int id, CancellationToken ct = default);
}

public record ResultadoReenvio(AvisoResumenDto? Aviso, string? Error)
{
    public bool Exito => Aviso is not null;
}

// Incluye la clave HMAC que se muestra una sola vez.
public record ResultadoCreacionSuscripcion(
    SuscripcionAvisosCreadaDto? Suscripcion, string? Error)
{
    public bool Exito => Suscripcion is not null;
}

// No contiene la clave secreta.
public record ResultadoSuscripcion(
    SuscripcionAvisosDto? Suscripcion, string? Error)
{
    public bool Exito => Suscripcion is not null;
}