using System.Net;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.CommercePortal.Servicios;

public class DevolucionesApiHttp(HttpClient http, SesionActual sesion) : ClienteApiBase(http, sesion), IDevolucionesApi
{
    public async Task<List<DevolucionComercioDto>> ListarAsync(bool soloPendientes, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"api/envios/devoluciones?pendientes={(soloPendientes ? "true" : "false")}");
        using var respuesta = await EnviarAsync(pedido, ct);

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<List<DevolucionComercioDto>>(ct) ?? [];
    }

    public async Task<string?> ConfirmarRecibidaAsync(int envioId, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"api/envios/{envioId}/devolucion/recibida");
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            return await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct);

        respuesta.EnsureSuccessStatusCode();
        return null;
    }
}
