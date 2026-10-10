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

        return "api/envios?" + string.Join('&', parametros);
    }
}
