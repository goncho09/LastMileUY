using LastMileUY.Contracts.Destinatarios;

namespace LastMileUY.CommercePortal.Servicios;

// Destinatarios del comercio, con sus direcciones: el listado para el alta de envíos y el alta
// de destinatarios y de direcciones nuevas
public interface IDestinatariosApi
{
    Task<List<DestinatarioDto>> ListarAsync(CancellationToken ct = default);

    Task<ResultadoAltaDestinatario> CrearAsync(CrearDestinatarioRequest request, CancellationToken ct = default);

    Task<ResultadoAltaDireccion> AgregarDireccionAsync(
        int destinatarioId, CrearDireccionRequest request, CancellationToken ct = default);
}

// O se creó, o la API lo rechazó con un mensaje para mostrar
public record ResultadoAltaDestinatario(DestinatarioDto? Destinatario, string? Error)
{
    public bool Exito => Destinatario is not null;
}

public record ResultadoAltaDireccion(DireccionDto? Direccion, string? Error)
{
    public bool Exito => Direccion is not null;
}
