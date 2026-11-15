using System.Net;
using LastMileUY.Contracts.Seguimiento;

namespace LastMileUY.PublicTracking.Servicios;

public class SeguimientoApi(HttpClient http)
{
    public async Task<SeguimientoPublicoDto?> ObtenerAsync(string codigo, CancellationToken cancellationToken = default)
    {
        using var respuesta = await http.GetAsync(
            $"api/seguimiento/{Uri.EscapeDataString(codigo)}", cancellationToken);

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ConsultasExcedidasException();

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<SeguimientoPublicoDto>(cancellationToken);
    }
    public async Task<string?> SolicitarReprogramacionAsync(
        string codigo, SolicitarReprogramacionRequest pedido, CancellationToken cancellationToken = default)
    {
        using var respuesta = await http.PostAsJsonAsync(
            $"api/seguimiento/{Uri.EscapeDataString(codigo)}/reprogramacion", pedido, cancellationToken);

        if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ConsultasExcedidasException();

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return "No encontramos ese envío.";

        if (respuesta.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            try
            {
                return (await respuesta.Content.ReadFromJsonAsync<RespuestaError>(cancellationToken))?.Mensaje
                       ?? "No se pudo reprogramar la entrega.";
            }
            catch (System.Text.Json.JsonException)
            {
                return "No se pudo reprogramar la entrega.";
            }
        }

        respuesta.EnsureSuccessStatusCode();
        return null;
    }

    private sealed record RespuestaError(string? Mensaje);

}

public class ConsultasExcedidasException() : Exception("Se hicieron demasiadas consultas seguidas.");
