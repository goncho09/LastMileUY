using System.Text.Json;

namespace LastMileUY.Backoffice.Servicios;

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
        catch (JsonException)
        {
        }

        return porDefecto;
    }

    private record MensajeDeError(string? Mensaje);
}
