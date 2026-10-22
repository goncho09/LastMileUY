using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.CommercePortal.Servicios;

// POST api/envios (alta) y GET api/envios (listado).
// Alta: respuestas posibles: 201 creado, 400 datos inválidos,
// 409 referencia externa repetida, 401 sesión vencida (lanza SesionVencidaException).
public class EnviosApiHttp(HttpClient http, SesionActual sesion)
    : ClienteApiBase(http, sesion), IEnviosApi
{
    public async Task<ResultadoAlta> CrearAsync(CrearEnvioRequest request, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "api/envios")
        {
            Content = JsonContent.Create(request)
        };

        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            return new ResultadoAlta(null, await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));
        }

        respuesta.EnsureSuccessStatusCode();

        var envio = await respuesta.Content.ReadFromJsonAsync<EnvioResponse>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el envío creado.");

        return new ResultadoAlta(envio, null);
    }

    public async Task<PaginaDto<EnvioResumenDto>> ListarAsync(
        int pagina, int tamanioPagina, string? estado, string? buscar, CancellationToken ct = default)
    {
        var url = $"api/envios?pagina={pagina}&tamanioPagina={tamanioPagina}";

        if (!string.IsNullOrWhiteSpace(estado))
            url += $"&estado={Uri.EscapeDataString(estado)}";

        if (!string.IsNullOrWhiteSpace(buscar))
            url += $"&buscar={Uri.EscapeDataString(buscar.Trim())}";

        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        using var respuesta = await EnviarAsync(pedido, ct);

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<PaginaDto<EnvioResumenDto>>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el listado de envíos.");
    }
}
