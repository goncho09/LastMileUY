using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real por HTTP (/api/rutas)
public class RutasApiHttp(HttpClient http) : IRutasApi
{
    public async Task<PaginaDto<RutaResumenDto>> ListarAsync(FiltroRutas filtro, CancellationToken ct = default)
    {
        var pagina = await http.GetFromJsonAsync<PaginaDto<RutaResumenDto>>(ArmarUrlListado(filtro), ct);

        return pagina ?? new PaginaDto<RutaResumenDto>([], 0, filtro.Pagina, filtro.TamanioPagina);
    }

    public async Task<RutaDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"api/rutas/{id}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<RutaDetalleDto>(ct);
    }

    public async Task<OpcionesRutaDto> ObtenerOpcionesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones", ct)
        ?? new OpcionesRutaDto([], [], []);

    public async Task<RutaDetalleDto> CrearAsync(CrearRutaRequest ruta, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync("api/rutas", ruta, ct);

        await RechazarSiCorrespondeAsync(respuesta, "armar hojas de ruta", ct);
        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<RutaDetalleDto>(ct))!;
    }

    public async Task<RutaDetalleDto?> DespacharAsync(int id, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsync($"api/rutas/{id}/despachar", null, ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await RechazarSiCorrespondeAsync(respuesta, "despachar hojas de ruta", ct);
        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<RutaDetalleDto>(ct);
    }

    public static string ArmarUrlListado(FiltroRutas filtro)
    {
        var parametros = new List<string>
        {
            $"pagina={filtro.Pagina}",
            $"tamanioPagina={filtro.TamanioPagina}"
        };

        if (filtro.Fecha is { } fecha)
        {
            parametros.Add($"fecha={fecha:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            parametros.Add($"estado={Uri.EscapeDataString(filtro.Estado)}");
        }

        return "api/rutas?" + string.Join('&', parametros);
    }

    // 400 y 409 traen el motivo para mostrarle al usuario; 403 es falta de permiso
    private static async Task RechazarSiCorrespondeAsync(
        HttpResponseMessage respuesta, string accion, CancellationToken ct)
    {
        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException($"Tu usuario no tiene permiso para {accion}.");
            case HttpStatusCode.BadRequest or HttpStatusCode.Conflict:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "La API rechazó la hoja de ruta.", ct));
        }
    }
}
