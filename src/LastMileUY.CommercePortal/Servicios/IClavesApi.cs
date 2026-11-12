using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.CommercePortal.Servicios;

// Claves de la API pública del comercio (pantalla Integración, docs/api-publica.md)
public interface IClavesApi
{
    Task<List<ClaveApiDto>> ListarAsync(CancellationToken ct = default);

    Task<ResultadoClaveCreada> CrearAsync(string nombre, CancellationToken ct = default);

    Task<ClaveApiDto?> RevocarAsync(int id, CancellationToken ct = default);
}

// La clave completa viaja solo acá: se muestra una vez
public record ResultadoClaveCreada(ClaveApiCreadaDto? Creada, string? Error)
{
    public bool Exito => Creada is not null;
}
