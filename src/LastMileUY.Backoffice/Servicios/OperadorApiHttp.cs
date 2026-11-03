using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real. Si la sesión venció, ReenviarCookieApi lanza SesionVencidaException.
public class OperadorApiHttp(HttpClient http) : IOperadorApi
{
    private const string RutaReglasEntregaParcial = "api/operador/reglas-entrega-parcial";

    public async Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<OperadorActualDto>("api/operador", ct)
        ?? throw new InvalidOperationException("La API no devolvió los datos del operador.");

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
