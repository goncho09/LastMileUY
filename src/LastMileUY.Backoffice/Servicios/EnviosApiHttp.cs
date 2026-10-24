using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real por HTTP (GET /api/envios y GET /api/envios/{id})
public class EnviosApiHttp(HttpClient http) : IEnviosApi
{
    public async Task<PaginaDto<EnvioResumenDto>> ListarAsync(FiltroEnvios filtro, CancellationToken ct = default)
    {
        var pagina = await http.GetFromJsonAsync<PaginaDto<EnvioResumenDto>>(ArmarUrlListado(filtro), ct);

        return pagina ?? new PaginaDto<EnvioResumenDto>([], 0, filtro.Pagina, filtro.TamanioPagina);
    }

    public async Task<EnvioDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync($"api/envios/{id}", ct);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<EnvioDetalleDto>(ct);
    }

    public async Task<EnvioDetalleDto?> CambiarEstadoAsync(
        int id, string estado, string? descripcion, CancellationToken ct = default)
    {
        using var respuesta = await http.PatchAsJsonAsync(
            $"api/envios/{id}/estado",
            new CambiarEstadoRequest { Estado = estado, Descripcion = descripcion },
            ct);

        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException("Tu usuario no tiene permiso para cambiar el estado de los envíos.");
            case HttpStatusCode.BadRequest or HttpStatusCode.Conflict:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "La API rechazó el cambio.", ct));
        }

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<EnvioDetalleDto>(ct);
    }
    
    public async Task<List<ActividadEnvioDto>> ObtenerActividadRecienteAsync(
        CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<ActividadEnvioDto>>(
            "api/envios/actividad-reciente", ct) ?? [];
    }

    public static string ArmarUrlListado(FiltroEnvios filtro)
    {
        var parametros = new List<string>
        {
            $"pagina={filtro.Pagina}",
            $"tamanioPagina={filtro.TamanioPagina}"
        };

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            parametros.Add($"estado={Uri.EscapeDataString(filtro.Estado)}");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            parametros.Add($"buscar={Uri.EscapeDataString(filtro.Buscar.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Canal))
        {
            parametros.Add($"canal={Uri.EscapeDataString(filtro.Canal)}");
        }

        return "api/envios?" + string.Join('&', parametros);
    }
}
