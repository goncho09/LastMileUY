using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real por HTTP (/api/sincronizacion)
public class SincronizacionApiHttp(HttpClient http) : ISincronizacionApi
{
    public async Task<PaginaDto<OperacionSincronizadaDto>> ListarAsync(
        FiltroOperaciones filtro, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<PaginaDto<OperacionSincronizadaDto>>(ArmarUrlListado(filtro), ct)
        ?? new PaginaDto<OperacionSincronizadaDto>([], 0, filtro.Pagina, filtro.TamanioPagina);

    public async Task<Evidencia?> ObtenerEvidenciaAsync(int intentoEntregaId, string tipo, CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync(
            $"api/sincronizacion/evidencias/{intentoEntregaId}/{Uri.EscapeDataString(tipo)}", ct);

        if (respuesta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        respuesta.EnsureSuccessStatusCode();

        return new Evidencia(
            await respuesta.Content.ReadAsByteArrayAsync(ct),
            respuesta.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public static string ArmarUrlListado(FiltroOperaciones filtro)
    {
        var parametros = new List<string>
        {
            $"pagina={filtro.Pagina}",
            $"tamanioPagina={filtro.TamanioPagina}"
        };

        if (filtro.RutaId is { } rutaId)
        {
            parametros.Add($"rutaId={rutaId}");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Resultado))
        {
            parametros.Add($"resultado={Uri.EscapeDataString(filtro.Resultado)}");
        }

        return "api/sincronizacion/operaciones?" + string.Join('&', parametros);
    }
}
