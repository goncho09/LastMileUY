using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Comun;

namespace LastMileUY.CommercePortal.Servicios;

// Avisos HTTP del comercio: sus suscripciones (api/avisos/suscripciones) y el historial de entregas,
// con el reenvío de los fallidos (api/avisos)
public interface IAvisosApi
{
    Task<List<SuscripcionAvisosDto>> ListarSuscripcionesAsync(CancellationToken ct = default);

    Task<ResultadoSuscripcion> CrearSuscripcionAsync(CrearSuscripcionAvisosRequest request, CancellationToken ct = default);

    Task<ResultadoSuscripcion> CambiarActivaAsync(int id, bool activa, CancellationToken ct = default);

    Task<PaginaDto<AvisoResumenDto>> ListarAvisosAsync(int pagina, int tamanioPagina, string? estado, CancellationToken ct = default);

    // null si el aviso ya no existe
    Task<AvisoDetalleDto?> ObtenerAvisoAsync(int id, CancellationToken ct = default);

    Task<ResultadoReenvio> ReenviarAsync(int id, CancellationToken ct = default);
}

// O se reenvió (el aviso queda Pendiente), o la API lo rechazó con un mensaje para mostrar
public record ResultadoReenvio(AvisoResumenDto? Aviso, string? Error)
{
    public bool Exito => Aviso is not null;
}

// O salió bien, o la API lo rechazó con un mensaje para mostrar
public record ResultadoSuscripcion(SuscripcionAvisosDto? Suscripcion, string? Error)
{
    public bool Exito => Suscripcion is not null;
}
