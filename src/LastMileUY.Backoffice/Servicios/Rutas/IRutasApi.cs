using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

public interface IRutasApi
{
    Task<PaginaDto<RutaResumenDto>> ListarAsync(FiltroRutas filtro, CancellationToken ct = default);

    Task<RutaDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<OpcionesRutaDto> ObtenerOpcionesAsync(CancellationToken ct = default);

    Task<RutaDetalleDto> CrearAsync(CrearRutaRequest ruta, CancellationToken ct = default);

    Task<SugerenciaOrdenDto> SugerirOrdenAsync(IReadOnlyList<int> envioIds, CancellationToken ct = default);

    Task<RutaDetalleDto?> DespacharAsync(int id, CancellationToken ct = default);
}

public record FiltroRutas(
    DateOnly? Fecha = null,
    string? Estado = null,
    int Pagina = 1,
    int TamanioPagina = 20);
