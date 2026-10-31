using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.Backoffice.Servicios;

public class MensajesFallidosApiHttp(HttpClient http)
    : IMensajesFallidosApi
{
    public async Task<ResumenMensajesFallidosDto> ObtenerResumenAsync(
        CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<ResumenMensajesFallidosDto>(
                   "api/mensajeria/fallidos/resumen", ct)
               ?? throw new InvalidOperationException(
                   "La API no devolvió el resumen de mensajes fallidos.");
    }

    public async Task<PaginaResponse<MensajeFallidoResponse>> ListarAsync(
        FiltroMensajesFallidos filtro,
        CancellationToken ct = default)
    {
        var parametros = new List<string>
        {
            $"pagina={filtro.Pagina}",
            $"tamanoPagina={filtro.TamanoPagina}"
        };

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            parametros.Add(
                $"estado={Uri.EscapeDataString(filtro.Estado)}");
        }

        if (filtro.Desde.HasValue)
        {
            parametros.Add(
                $"desde={Uri.EscapeDataString(filtro.Desde.Value.ToString("O"))}");
        }

        if (filtro.Hasta.HasValue)
        {
            parametros.Add(
                $"hasta={Uri.EscapeDataString(filtro.Hasta.Value.ToString("O"))}");
        }

        var url = "api/mensajeria/fallidos?" +
                  string.Join("&", parametros);

        return await http.GetFromJsonAsync<PaginaResponse<MensajeFallidoResponse>>(
                   url, ct)
               ?? throw new InvalidOperationException(
                   "La API no devolvió el listado de mensajes fallidos.");
    }

    public async Task<MensajeFallidoDetalleResponse?> ObtenerDetalleAsync(
        Guid id,
        CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync(
            $"api/mensajeria/fallidos/{id}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content
            .ReadFromJsonAsync<MensajeFallidoDetalleResponse>(
                cancellationToken: ct);
    }
}