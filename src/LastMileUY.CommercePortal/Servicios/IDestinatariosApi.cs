using LastMileUY.Contracts.Destinatarios;

namespace LastMileUY.CommercePortal.Servicios;

public interface IDestinatariosApi
{
    Task<List<DestinatarioDto>> ListarAsync(CancellationToken ct = default);

    Task<ResultadoAltaDestinatario> CrearAsync(CrearDestinatarioRequest request, CancellationToken ct = default);

    Task<ResultadoAltaDireccion> AgregarDireccionAsync(
        int destinatarioId, CrearDireccionRequest request, CancellationToken ct = default);
}

public record ResultadoAltaDestinatario(DestinatarioDto? Destinatario, string? Error)
{
    public bool Exito => Destinatario is not null;
}

public record ResultadoAltaDireccion(DireccionDto? Direccion, string? Error)
{
    public bool Exito => Direccion is not null;
}
