using System.Net;
using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.CommercePortal.Servicios;

// Claves de la API pública: la API solo deja ver y tocar las del comercio y el operador de la sesión
public class ClavesApiHttp(HttpClient http, SesionActual sesion)
    : ClienteApiBase(http, sesion), IClavesApi
{
    private const string Ruta = "api/claves-api";

    public async Task<List<ClaveApiDto>> ListarAsync(CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, Ruta);
        using var respuesta = await EnviarAsync(pedido, ct);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<List<ClaveApiDto>>(ct) ?? [];
    }

    public async Task<ResultadoClaveCreada> CrearAsync(string nombre, string ambiente, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(new CrearClaveApiRequest { Nombre = nombre, Ambiente = ambiente })
        };
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            return new ResultadoClaveCreada(null, await AutenticacionApiHttp.LeerMensajeAsync(respuesta, ct));

        respuesta.EnsureSuccessStatusCode();
        var creada = await respuesta.Content.ReadFromJsonAsync<ClaveApiCreadaDto>(ct)
                     ?? throw new InvalidOperationException("La API no devolvió la clave creada.");

        return new ResultadoClaveCreada(creada, null);
    }

    // null si no existe o no es del comercio
    public async Task<ClaveApiDto?> RevocarAsync(int id, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{Ruta}/{id}/revocar");
        using var respuesta = await EnviarAsync(pedido, ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<ClaveApiDto>(ct);
    }
}
