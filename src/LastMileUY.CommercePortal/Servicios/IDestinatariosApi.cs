using LastMileUY.Contracts.Destinatarios;

namespace LastMileUY.CommercePortal.Servicios;

// Destinatarios del comercio, con sus direcciones, para el alta de envíos
public interface IDestinatariosApi
{
    Task<List<DestinatarioDto>> ListarAsync(CancellationToken ct = default);
}
