using System.Net.Http.Json;

namespace LastMileUY.Backoffice.Servicios;

// Lectura de las respuestas de error de la API, que vienen como { "mensaje": "..." }
public static class RespuestasApi
{
    public static async Task<string> LeerMensajeAsync(
        HttpResponseMessage respuesta, string porDefecto, CancellationToken ct)
    {
        try
        {
            var error = await respuesta.Content.ReadFromJsonAsync<MensajeDeError>(ct);

            if (!string.IsNullOrWhiteSpace(error?.Mensaje))
            {
                return error.Mensaje;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Sin cuerpo o con otro formato: se usa el mensaje genérico
        }

        return porDefecto;
    }

    private record MensajeDeError(string? Mensaje);
}
