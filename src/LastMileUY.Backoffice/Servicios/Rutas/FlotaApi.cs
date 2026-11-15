using System.Net.Http.Json;
using LastMileUY.Contracts.Flota;

namespace LastMileUY.Backoffice.Servicios;

public interface IFlotaApi
{
    Task<FlotaDto> ObtenerAsync(CancellationToken ct = default);
}

public sealed class FlotaApiHttp(HttpClient http) : IFlotaApi
{
    public async Task<FlotaDto> ObtenerAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<FlotaDto>("api/flota", ct)
        ?? throw new InvalidOperationException("La API no devolvió la flota.");
}

public sealed class FlotaApiDePrueba : IFlotaApi
{
    public Task<FlotaDto> ObtenerAsync(CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        return Task.FromResult(new FlotaDto(
            ahora,
            [
                new VehiculoEnRutaDto(1, "Centro mañana", "Carlos Méndez", "SBC 1234", "EnCurso", 8, 3,
                    -34.9058m, -56.1913m, ahora.AddMinutes(-2)),
                new VehiculoEnRutaDto(2, "Pocitos tarde", "Lucía Fernández", "SAB 5678", "Despachada", 6, 6,
                    -34.9145m, -56.1500m, ahora.AddMinutes(-25)),
                new VehiculoEnRutaDto(3, "Carrasco", "Martín Rodríguez", "SCD 9012", "Despachada", 5, 5, null, null, null)
            ],
            [
                new EnvioEnRiesgoDto(601, "KX4M7P2QW9TA", 1, "Centro mañana", "Lucía Pérez",
                    "Franja Mañana hasta las 12:00", ahora.AddMinutes(-15), true),
                new EnvioEnRiesgoDto(602, "PL8R3N6YD2HC", 2, "Pocitos tarde", "Martín Rodríguez",
                    "Plazo comprometido de 24 h", ahora.AddMinutes(40), false)
            ]));
    }
}
