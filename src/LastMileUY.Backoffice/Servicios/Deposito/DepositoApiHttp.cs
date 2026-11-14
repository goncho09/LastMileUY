using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Deposito;

namespace LastMileUY.Backoffice.Servicios;

public sealed class DepositoApiHttp(HttpClient http) : IDepositoApi
{
    private const string Base = "api/deposito";

    public async Task<IReadOnlyList<EnvioPorRecibirDto>> PorRecibirAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<EnvioPorRecibirDto>>($"{Base}/por-recibir", ct) ?? [];

    public async Task<EnvioParaRecibirDto?> BuscarAsync(string codigo, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"{Base}/recepcion/{Uri.EscapeDataString(codigo.Trim())}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<EnvioParaRecibirDto>(ct);
    }

    public async Task<RecepcionDto> RecibirAsync(RegistrarRecepcionRequest request, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync($"{Base}/recepciones", request, ct);
        await VerificarAsync(respuesta, ct);

        return await respuesta.Content.ReadFromJsonAsync<RecepcionDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió la recepción.");
    }

    public async Task<IReadOnlyList<RecepcionDto>> DiscrepanciasAsync(bool soloPendientes, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<RecepcionDto>>(
            $"{Base}/discrepancias?pendientes={(soloPendientes ? "true" : "false")}", ct) ?? [];

    public async Task ResolverAsync(int recepcionId, string resolucion, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync(
            $"{Base}/discrepancias/{recepcionId}/resolver", new ResolverDiscrepanciaRequest(resolucion), ct);
        await VerificarAsync(respuesta, ct);
    }

    public async Task<IReadOnlyList<RutaPorCargarDto>> RutasPorCargarAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<RutaPorCargarDto>>($"{Base}/cargas", ct) ?? [];

    public async Task<CargaRutaDto?> CargaAsync(int rutaId, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"{Base}/cargas/{rutaId}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<CargaRutaDto>(ct);
    }

    public async Task<CargaRutaDto> VerificarCargaAsync(int rutaId, VerificarCargaRequest request, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync($"{Base}/cargas/{rutaId}/verificaciones", request, ct);
        await VerificarAsync(respuesta, ct);

        return await respuesta.Content.ReadFromJsonAsync<CargaRutaDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió la carga.");
    }

    public async Task QuitarEnvioAsync(int rutaId, int envioId, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsync($"{Base}/cargas/{rutaId}/envios/{envioId}/quitar", null, ct);
        await VerificarAsync(respuesta, ct);
    }

    private static async Task VerificarAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException("No tenés permisos para trabajar en el depósito.");
            case HttpStatusCode.NotFound:
                throw new OperacionRechazadaException("El envío ya no existe. Volvé a buscarlo.");
            case HttpStatusCode.BadRequest or HttpStatusCode.Conflict:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "Los datos no son válidos.", ct));
        }

        respuesta.EnsureSuccessStatusCode();
    }
}
