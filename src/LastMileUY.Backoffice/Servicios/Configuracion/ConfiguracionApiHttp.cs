using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Configuracion;

namespace LastMileUY.Backoffice.Servicios;

public sealed class ConfiguracionApiHttp(HttpClient http) : IConfiguracionApi
{
    private const string Base = "api/operador";

    public Task<IReadOnlyList<ZonaDto>> ListarZonasAsync(CancellationToken ct = default) =>
        ListarAsync<ZonaDto>("zonas", ct);

    public Task CrearZonaAsync(GuardarZonaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Post, "zonas", request, ct);

    public Task EditarZonaAsync(int id, GuardarZonaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"zonas/{id}", request, ct);

    public Task CambiarZonaActivaAsync(int id, bool activa, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"zonas/{id}/activa", new CambiarActivoRequest(activa), ct);

    public Task CrearFranjaAsync(int zonaId, GuardarFranjaHorariaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Post, $"zonas/{zonaId}/franjas", request, ct);

    public Task EditarFranjaAsync(int zonaId, int id, GuardarFranjaHorariaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"zonas/{zonaId}/franjas/{id}", request, ct);

    public Task CambiarFranjaActivaAsync(int zonaId, int id, bool activa, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"zonas/{zonaId}/franjas/{id}/activa", new CambiarActivoRequest(activa), ct);

    public Task<IReadOnlyList<VehiculoConfiguracionDto>> ListarVehiculosAsync(CancellationToken ct = default) =>
        ListarAsync<VehiculoConfiguracionDto>("vehiculos", ct);

    public Task CrearVehiculoAsync(GuardarVehiculoRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Post, "vehiculos", request, ct);

    public Task EditarVehiculoAsync(int id, GuardarVehiculoRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"vehiculos/{id}", request, ct);

    public Task CambiarVehiculoActivoAsync(int id, bool activo, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"vehiculos/{id}/activo", new CambiarActivoRequest(activo), ct);

    public Task<IReadOnlyList<MotivoNoEntregaConfiguracionDto>> ListarMotivosAsync(CancellationToken ct = default) =>
        ListarAsync<MotivoNoEntregaConfiguracionDto>("motivos-no-entrega", ct);

    public Task CrearMotivoAsync(GuardarMotivoNoEntregaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Post, "motivos-no-entrega", request, ct);

    public Task EditarMotivoAsync(int id, GuardarMotivoNoEntregaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"motivos-no-entrega/{id}", request, ct);

    public Task CambiarMotivoActivoAsync(int id, bool activo, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"motivos-no-entrega/{id}/activo", new CambiarActivoRequest(activo), ct);

    public Task<IReadOnlyList<ReglaOperativaConfiguracionDto>> ListarReglasAsync(CancellationToken ct = default) =>
        ListarAsync<ReglaOperativaConfiguracionDto>("reglas-operativas", ct);

    public Task CrearReglaAsync(GuardarReglaOperativaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Post, "reglas-operativas", request, ct);

    public Task EditarReglaAsync(int id, GuardarReglaOperativaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"reglas-operativas/{id}", request, ct);

    public Task CambiarReglaActivaAsync(int id, bool activa, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"reglas-operativas/{id}/activa", new CambiarActivoRequest(activa), ct);

    public Task<IReadOnlyList<CuadroTarifarioDto>> ListarCuadrosAsync(CancellationToken ct = default) =>
        ListarAsync<CuadroTarifarioDto>("cuadros-tarifarios", ct);

    public async Task<CuadroTarifarioDto?> ObtenerCuadroAsync(int id, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"{Base}/cuadros-tarifarios/{id}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<CuadroTarifarioDto>(ct);
    }

    public async Task<CuadroTarifarioDto> CrearCuadroAsync(CrearCuadroTarifarioRequest request, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync($"{Base}/cuadros-tarifarios", request, ct);
        await VerificarAsync(respuesta, ct);

        return await respuesta.Content.ReadFromJsonAsync<CuadroTarifarioDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el cuadro creado.");
    }

    public Task AgregarTarifaAsync(int cuadroId, GuardarTarifaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Post, $"cuadros-tarifarios/{cuadroId}/tarifas", request, ct);

    public Task EditarTarifaAsync(int cuadroId, int id, GuardarTarifaRequest request, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"cuadros-tarifarios/{cuadroId}/tarifas/{id}", request, ct);

    public Task CambiarTarifaActivaAsync(int cuadroId, int id, bool activa, CancellationToken ct = default) =>
        EnviarAsync(HttpMethod.Put, $"cuadros-tarifarios/{cuadroId}/tarifas/{id}/activa", new CambiarActivoRequest(activa), ct);

    private async Task<IReadOnlyList<T>> ListarAsync<T>(string ruta, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<T>>($"{Base}/{ruta}", ct) ?? [];

    private async Task EnviarAsync<T>(HttpMethod metodo, string ruta, T cuerpo, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(metodo, $"{Base}/{ruta}") { Content = JsonContent.Create(cuerpo) };
        using var respuesta = await http.SendAsync(pedido, ct);
        await VerificarAsync(respuesta, ct);
    }

    private static async Task VerificarAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException("No tenés permisos para cambiar la configuración del operador.");
            case HttpStatusCode.NotFound:
                throw new OperacionRechazadaException("Lo que querías cambiar ya no existe. Recargá la página.");
            case HttpStatusCode.BadRequest or HttpStatusCode.Conflict:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "Los datos no son válidos.", ct));
        }

        respuesta.EnsureSuccessStatusCode();
    }
}
