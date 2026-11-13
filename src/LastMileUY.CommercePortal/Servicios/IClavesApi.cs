using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.CommercePortal.Servicios;

public interface IClavesApi
{
    Task<List<ClaveApiDto>> ListarAsync(CancellationToken ct = default);

    Task<ResultadoClaveCreada> CrearAsync(string nombre, string ambiente, CancellationToken ct = default);

    Task<ClaveApiDto?> RevocarAsync(int id, CancellationToken ct = default);
}

public record ResultadoClaveCreada(ClaveApiCreadaDto? Creada, string? Error)
{
    public bool Exito => Creada is not null;
}
