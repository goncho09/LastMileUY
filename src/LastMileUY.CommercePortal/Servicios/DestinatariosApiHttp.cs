using System.Net.Http.Json;
using LastMileUY.Contracts.Destinatarios;

namespace LastMileUY.CommercePortal.Servicios;

// GET api/destinatarios: la API ya devuelve solo los del comercio del usuario
public class DestinatariosApiHttp(HttpClient http, SesionActual sesion)
    : ClienteApiBase(http, sesion), IDestinatariosApi
{
    public async Task<List<DestinatarioDto>> ListarAsync(CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, "api/destinatarios");
        using var respuesta = await EnviarAsync(pedido, ct);

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<List<DestinatarioDto>>(ct) ?? [];
    }
}
