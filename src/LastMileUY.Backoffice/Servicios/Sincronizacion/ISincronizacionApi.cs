using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.Backoffice.Servicios;

public interface ISincronizacionApi
{
    Task<PaginaDto<OperacionSincronizadaDto>> ListarAsync(FiltroOperaciones filtro, CancellationToken ct = default);

    Task<Evidencia?> ObtenerEvidenciaAsync(int intentoEntregaId, string tipo, CancellationToken ct = default);
}

public record FiltroOperaciones(
    int? RutaId = null,
    string? Resultado = null,
    int Pagina = 1,
    int TamanioPagina = 20);

public record Evidencia(byte[] Contenido, string TipoContenido);
