using LastMileUY.Contracts.Avisos;

namespace LastMileUY.CommercePortal.Servicios;

// Suscripciones del comercio a avisos HTTP (api/avisos/suscripciones)
public interface IAvisosApi
{
    Task<List<SuscripcionAvisosDto>> ListarSuscripcionesAsync(CancellationToken ct = default);

    Task<ResultadoSuscripcion> CrearSuscripcionAsync(CrearSuscripcionAvisosRequest request, CancellationToken ct = default);

    Task<ResultadoSuscripcion> CambiarActivaAsync(int id, bool activa, CancellationToken ct = default);
}

// O salió bien, o la API lo rechazó con un mensaje para mostrar
public record ResultadoSuscripcion(SuscripcionAvisosDto? Suscripcion, string? Error)
{
    public bool Exito => Suscripcion is not null;
}
