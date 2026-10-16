using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

// Lo que el Backoffice necesita de la API sobre hojas de ruta (ver docs/hojas-de-ruta.md).
// Igual que con los envíos: una implementación con datos de prueba y otra que llama a la API real.
public interface IRutasApi
{
    Task<PaginaDto<RutaResumenDto>> ListarAsync(FiltroRutas filtro, CancellationToken ct = default);

    // Devuelve null si la ruta no existe (o no es del operador)
    Task<RutaDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default);

    // Repartidores, vehículos y envíos que se pueden usar en una ruta nueva
    Task<OpcionesRutaDto> ObtenerOpcionesAsync(CancellationToken ct = default);

    // OperacionRechazadaException: la API no aceptó la ruta (capacidad, franjas, duplicados, sin permiso)
    Task<RutaDetalleDto> CrearAsync(CrearRutaRequest ruta, CancellationToken ct = default);

    // Devuelve la ruta despachada, o null si no existe.
    // OperacionRechazadaException: no se puede despachar (ya salió, un envío ya no está asignado, sin permiso)
    Task<RutaDetalleDto?> DespacharAsync(int id, CancellationToken ct = default);
}

// Lo que el usuario elige en el listado de rutas
public record FiltroRutas(
    DateOnly? Fecha = null,
    string? Estado = null,
    int Pagina = 1,
    int TamanioPagina = 20);
