using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.Backoffice.Servicios;

// Lo que llegó desde la app del repartidor (ver docs/sincronizacion.md): el historial de operaciones
// con su resultado y las imágenes de evidencia. Igual que los demás: datos de prueba o API real.
public interface ISincronizacionApi
{
    Task<PaginaDto<OperacionSincronizadaDto>> ListarAsync(FiltroOperaciones filtro, CancellationToken ct = default);

    // La firma o la foto de una entrega; null si no existe (o es de otro operador)
    Task<Evidencia?> ObtenerEvidenciaAsync(int intentoEntregaId, string tipo, CancellationToken ct = default);
}

public record FiltroOperaciones(
    int? RutaId = null,
    string? Resultado = null,
    int Pagina = 1,
    int TamanioPagina = 20);

public record Evidencia(byte[] Contenido, string TipoContenido);
