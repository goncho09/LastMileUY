using LastMileUY.Contracts.Configuracion;

namespace LastMileUY.Backoffice.Servicios;

public interface IConfiguracionApi
{
    Task<IReadOnlyList<ZonaDto>> ListarZonasAsync(CancellationToken ct = default);
    Task CrearZonaAsync(GuardarZonaRequest request, CancellationToken ct = default);
    Task EditarZonaAsync(int id, GuardarZonaRequest request, CancellationToken ct = default);
    Task CambiarZonaActivaAsync(int id, bool activa, CancellationToken ct = default);

    Task CrearFranjaAsync(int zonaId, GuardarFranjaHorariaRequest request, CancellationToken ct = default);
    Task EditarFranjaAsync(int zonaId, int id, GuardarFranjaHorariaRequest request, CancellationToken ct = default);
    Task CambiarFranjaActivaAsync(int zonaId, int id, bool activa, CancellationToken ct = default);

    Task<IReadOnlyList<VehiculoConfiguracionDto>> ListarVehiculosAsync(CancellationToken ct = default);
    Task CrearVehiculoAsync(GuardarVehiculoRequest request, CancellationToken ct = default);
    Task EditarVehiculoAsync(int id, GuardarVehiculoRequest request, CancellationToken ct = default);
    Task CambiarVehiculoActivoAsync(int id, bool activo, CancellationToken ct = default);

    Task<IReadOnlyList<MotivoNoEntregaConfiguracionDto>> ListarMotivosAsync(CancellationToken ct = default);
    Task CrearMotivoAsync(GuardarMotivoNoEntregaRequest request, CancellationToken ct = default);
    Task EditarMotivoAsync(int id, GuardarMotivoNoEntregaRequest request, CancellationToken ct = default);
    Task CambiarMotivoActivoAsync(int id, bool activo, CancellationToken ct = default);

    Task<IReadOnlyList<ReglaOperativaConfiguracionDto>> ListarReglasAsync(CancellationToken ct = default);
    Task CrearReglaAsync(GuardarReglaOperativaRequest request, CancellationToken ct = default);
    Task EditarReglaAsync(int id, GuardarReglaOperativaRequest request, CancellationToken ct = default);
    Task CambiarReglaActivaAsync(int id, bool activa, CancellationToken ct = default);

    Task<IReadOnlyList<CuadroTarifarioDto>> ListarCuadrosAsync(CancellationToken ct = default);
    Task<CuadroTarifarioDto?> ObtenerCuadroAsync(int id, CancellationToken ct = default);
    Task<CuadroTarifarioDto> CrearCuadroAsync(CrearCuadroTarifarioRequest request, CancellationToken ct = default);
    Task AgregarTarifaAsync(int cuadroId, GuardarTarifaRequest request, CancellationToken ct = default);
    Task EditarTarifaAsync(int cuadroId, int id, GuardarTarifaRequest request, CancellationToken ct = default);
    Task CambiarTarifaActivaAsync(int cuadroId, int id, bool activa, CancellationToken ct = default);
}
