
using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Avisos;

namespace LastMileUY.CommercePortal.Servicios;

// GET, POST y PATCH api/avisos/suscripciones.
// La API devuelve únicamente las suscripciones del comercio autenticado.
public class AvisosApiHttp(HttpClient http, SesionActual sesion)
    : ClienteApiBase(http, sesion), IAvisosApi
{
    private const string Ruta = "api/avisos/suscripciones";

    public async Task<List<SuscripcionAvisosDto>> ListarSuscripcionesAsync(
        CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, Ruta);
        using var respuesta = await EnviarAsync(pedido, ct);

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content
            .ReadFromJsonAsync<List<SuscripcionAvisosDto>>(ct) ?? [];
    }

    // Crear: devuelve la clave secreta HMAC.
    public async Task<ResultadoCreacionSuscripcion> CrearSuscripcionAsync(
        CrearSuscripcionAvisosRequest request,
        CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(request)
        };

        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
        {
            return new ResultadoCreacionSuscripcion(
                null,
                await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));
        }

        respuesta.EnsureSuccessStatusCode();

        var suscripcion = await respuesta.Content
            .ReadFromJsonAsync<SuscripcionAvisosCreadaDto>(ct)
            ?? throw new InvalidOperationException(
                "La API no devolvió la suscripción creada.");

        return new ResultadoCreacionSuscripcion(suscripcion, null);
    }

    // Activar o desactivar: no devuelve la clave secreta.
    public Task<ResultadoSuscripcion> CambiarActivaAsync(
        int id,
        bool activa,
        CancellationToken ct = default) =>
        EnviarConResultadoAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"{Ruta}/{id}")
            {
                Content = JsonContent.Create(
                    new CambiarSuscripcionAvisosRequest(activa))
            },
            ct);

    private async Task<ResultadoSuscripcion> EnviarConResultadoAsync(
        HttpRequestMessage pedido,
        CancellationToken ct)
    {
        using (pedido)
        using (var respuesta = await EnviarAsync(pedido, ct))
        {
            if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            {
                return new ResultadoSuscripcion(
                    null,
                    await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));
            }

            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                return new ResultadoSuscripcion(
                    null,
                    "La suscripción ya no existe.");
            }

            respuesta.EnsureSuccessStatusCode();

            var suscripcion = await respuesta.Content
                .ReadFromJsonAsync<SuscripcionAvisosDto>(ct)
                ?? throw new InvalidOperationException(
                    "La API no devolvió la suscripción.");

            return new ResultadoSuscripcion(suscripcion, null);
        }
    }
    
    
    public async Task<ResultadoCreacionSuscripcion> RegenerarClaveAsync(
        int id,
        CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Ruta}/{id}/regenerar-clave");

        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return new ResultadoCreacionSuscripcion(
                null,
                "La suscripción no existe o no pertenece a tu comercio.");
        }

        respuesta.EnsureSuccessStatusCode();

        var suscripcion = await respuesta.Content
                              .ReadFromJsonAsync<SuscripcionAvisosCreadaDto>(ct)
                          ?? throw new InvalidOperationException(
                              "La API no devolvió la nueva clave de la suscripción.");

        return new ResultadoCreacionSuscripcion(suscripcion, null);
    }

}
