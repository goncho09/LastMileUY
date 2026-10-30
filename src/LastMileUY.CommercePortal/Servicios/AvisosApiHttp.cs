using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Comun;

namespace LastMileUY.CommercePortal.Servicios;

// Suscripciones y claves HMAC, historial de avisos y reenvío de avisos fallidos.
// La API limita los resultados al comercio autenticado.
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
        return await respuesta.Content.ReadFromJsonAsync<List<SuscripcionAvisosDto>>(ct) ?? [];
    }

    public async Task<ResultadoCreacionSuscripcion> CrearSuscripcionAsync(
        CrearSuscripcionAvisosRequest request, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(request)
        };
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            return new ResultadoCreacionSuscripcion(
                null, await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));

        respuesta.EnsureSuccessStatusCode();
        var suscripcion = await respuesta.Content
            .ReadFromJsonAsync<SuscripcionAvisosCreadaDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió la suscripción creada.");

        return new ResultadoCreacionSuscripcion(suscripcion, null);
    }

    public Task<ResultadoSuscripcion> CambiarActivaAsync(
        int id, bool activa, CancellationToken ct = default) =>
        EnviarConResultadoAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"{Ruta}/{id}")
            {
                Content = JsonContent.Create(new CambiarSuscripcionAvisosRequest(activa))
            }, ct);

    public async Task<ResultadoCreacionSuscripcion> RegenerarClaveAsync(
        int id, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{Ruta}/{id}/regenerar-clave");
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return new ResultadoCreacionSuscripcion(
                null, "La suscripción no existe o no pertenece a tu comercio.");

        respuesta.EnsureSuccessStatusCode();
        var suscripcion = await respuesta.Content
            .ReadFromJsonAsync<SuscripcionAvisosCreadaDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió la nueva clave de la suscripción.");

        return new ResultadoCreacionSuscripcion(suscripcion, null);
    }

    public async Task<PaginaDto<AvisoResumenDto>> ListarAvisosAsync(
        int pagina, int tamanioPagina, string? estado, CancellationToken ct = default)
    {
        var url = $"api/avisos?pagina={pagina}&tamanioPagina={tamanioPagina}";
        if (!string.IsNullOrWhiteSpace(estado))
            url += $"&estado={Uri.EscapeDataString(estado)}";

        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        using var respuesta = await EnviarAsync(pedido, ct);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<PaginaDto<AvisoResumenDto>>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el listado de avisos.");
    }

    public async Task<AvisoDetalleDto?> ObtenerAvisoAsync(int id, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"api/avisos/{id}");
        using var respuesta = await EnviarAsync(pedido, ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<AvisoDetalleDto>(ct);
    }

    public async Task<ResultadoReenvio> ReenviarAsync(int id, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"api/avisos/{id}/reenviar");
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            return new ResultadoReenvio(null, await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return new ResultadoReenvio(null, "El aviso ya no existe.");

        respuesta.EnsureSuccessStatusCode();
        var aviso = await respuesta.Content.ReadFromJsonAsync<AvisoResumenDto>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el aviso.");
        return new ResultadoReenvio(aviso, null);
    }

    private async Task<ResultadoSuscripcion> EnviarConResultadoAsync(
        HttpRequestMessage pedido, CancellationToken ct)
    {
        using (pedido)
        using (var respuesta = await EnviarAsync(pedido, ct))
        {
            if (respuesta.StatusCode == HttpStatusCode.BadRequest)
                return new ResultadoSuscripcion(
                    null, await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return new ResultadoSuscripcion(null, "La suscripción ya no existe.");

            respuesta.EnsureSuccessStatusCode();
            var suscripcion = await respuesta.Content
                .ReadFromJsonAsync<SuscripcionAvisosDto>(ct)
                ?? throw new InvalidOperationException("La API no devolvió la suscripción.");
            return new ResultadoSuscripcion(suscripcion, null);
        }