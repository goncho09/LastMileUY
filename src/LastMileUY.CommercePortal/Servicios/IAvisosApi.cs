using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Comun;

namespace LastMileUY.CommercePortal.Servicios;

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

public record ResultadoCreacionSuscripcion(
    SuscripcionAvisosCreadaDto? Suscripcion, string? Error)
{
    public bool Exito => Suscripcion is not null;
}

public record ResultadoSuscripcion(
    SuscripcionAvisosDto? Suscripcion, string? Error)
{
    public bool Exito => Suscripcion is not null;
}