using System.Net;
using LastMileUY.Contracts.Reportes;

namespace LastMileUY.CommercePortal.Servicios;

public class CuentaApiHttp(HttpClient http, SesionActual sesion) : ClienteApiBase(http, sesion), ICuentaApi
{
    public async Task<ReporteGestionDto?> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"api/reportes/mi-cuenta?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}");
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            return null;

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<ReporteGestionDto>(ct);
    }
}
