using LastMileUY.Contracts.Destinatarios;
using System.Net;
namespace LastMileUY.CommercePortal.Servicios;

// GET api/destinatarios: la API ya devuelve solo los del comercio del usuario.
// POST api/destinatarios y api/destinatarios/{id}/direcciones: 201 creado, 400 datos inválidos
// (con el motivo), 403 o 404 si no es del comercio; 401 lanza SesionVencidaException.
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

    public async Task<ResultadoAltaDestinatario> CrearAsync(
        CrearDestinatarioRequest request, CancellationToken ct = default)
    {
        var (creado, error) = await PublicarAsync<DestinatarioDto>("api/destinatarios", request, ct);
        return new ResultadoAltaDestinatario(creado, error);
    }

    public async Task<ResultadoAltaDireccion> AgregarDireccionAsync(
        int destinatarioId, CrearDireccionRequest request, CancellationToken ct = default)
    {
        var (creada, error) = await PublicarAsync<DireccionDto>(
            $"api/destinatarios/{destinatarioId}/direcciones", request, ct);
        return new ResultadoAltaDireccion(creada, error);
    }

    private async Task<(T? Creado, string? Error)> PublicarAsync<T>(string url, object cuerpo, CancellationToken ct)
        where T : class
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(cuerpo) };
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            return (null, await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));

        if (respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return (null, "Ese destinatario no es de tu comercio.");

        respuesta.EnsureSuccessStatusCode();

        var creado = await respuesta.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidOperationException("La API no devolvió lo que se creó.");

        return (creado, null);
    }
}
