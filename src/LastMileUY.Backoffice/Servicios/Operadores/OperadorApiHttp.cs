using System.Net;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

public class OperadorApiHttp(HttpClient http) : IOperadorApi
{
    private const string RutaReglasEntregaParcial = "api/operador/reglas-entrega-parcial";

    public async Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<OperadorActualDto>("api/operador", ct)
        ?? throw new InvalidOperationException("La API no devolvió los datos del operador.");

    public async Task GuardarDepositoAsync(Coordenadas? deposito, CancellationToken ct = default)
    {
        using var respuesta = await http.PutAsJsonAsync("api/operador/deposito", new GuardarDepositoRequest
        {
            Latitud = deposito?.Latitud,
            Longitud = deposito?.Longitud
        }, ct);

        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException("Tu perfil no puede cambiar el depósito.");
            case HttpStatusCode.BadRequest:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "La API rechazó el depósito.", ct));
        }

        respuesta.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ReglaEntregaParcialDto>> ListarReglasEntregaParcialAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ReglaEntregaParcialDto>>(RutaReglasEntregaParcial, ct) ?? [];

    public async Task<ReglaEntregaParcialDto> PublicarReglaEntregaParcialAsync(
        PublicarReglaEntregaParcialRequest request, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync(RutaReglasEntregaParcial, request, ct);

        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException("Tu perfil no puede cambiar las reglas de entrega parcial.");
            case HttpStatusCode.BadRequest:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "La API rechazó las reglas.", ct));
        }

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<ReglaEntregaParcialDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió la versión publicada.");
    }
}
