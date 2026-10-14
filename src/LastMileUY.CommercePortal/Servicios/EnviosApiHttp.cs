using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.CommercePortal.Servicios;

// POST api/envios. Respuestas posibles: 201 creado, 400 datos inválidos,
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
}
