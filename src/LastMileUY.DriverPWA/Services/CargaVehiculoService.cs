using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Deposito;

namespace LastMileUY.DriverPWA.Services;

public enum ResultadoEnvioCarga
{
    Enviada,
    Guardada,
    Rechazada
}

public class CargaVehiculoService(HttpClient http, AlmacenLocal almacen, TimeProvider reloj)
{
    private const string ClavePendientes = "driver:cargas-pendientes";

    private static string ClaveEscaneados(int rutaId) => $"driver:carga:{rutaId}";

    private readonly SemaphoreSlim enviando = new(1, 1);

    public async Task<HashSet<string>> LeerEscaneadosAsync(int rutaId) =>
        (await almacen.LeerPendienteAsync<List<string>>(ClaveEscaneados(rutaId)) ?? []).ToHashSet();

    public Task GuardarEscaneadosAsync(int rutaId, IEnumerable<string> codigos) =>
        almacen.GuardarPendienteAsync(ClaveEscaneados(rutaId), codigos.Order().ToList());

    public async Task<bool> TienePendienteAsync(int rutaId) =>
        (await LeerPendientesAsync()).ContainsKey(rutaId);

    public async Task<(ResultadoEnvioCarga Resultado, string? Mensaje)> ConfirmarAsync(int rutaId, IEnumerable<string> codigos)
    {
        var pendientes = await LeerPendientesAsync();
        pendientes[rutaId] = new VerificarCargaRequest(codigos.Order().ToList(), null, reloj.GetUtcNow().UtcDateTime);
        await almacen.GuardarPendienteAsync(ClavePendientes, pendientes);

        return await EnviarAsync(rutaId);
    }

    public async Task EnviarPendientesAsync()
    {
        foreach (var rutaId in (await LeerPendientesAsync()).Keys.ToList())
            await EnviarAsync(rutaId);
    }

    private async Task<(ResultadoEnvioCarga, string?)> EnviarAsync(int rutaId)
    {
        await enviando.WaitAsync();

        try
        {
            var pendientes = await LeerPendientesAsync();

            if (!pendientes.TryGetValue(rutaId, out var pedido))
                return (ResultadoEnvioCarga.Enviada, null);

            HttpResponseMessage respuesta;

            try
            {
                respuesta = await http.PostAsJsonAsync($"api/rutas/mias/{rutaId}/carga", pedido);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (ResultadoEnvioCarga.Guardada, null);
            }

            using (respuesta)
            {
                if (respuesta.StatusCode == HttpStatusCode.Unauthorized || (int)respuesta.StatusCode >= 500)
                    return (ResultadoEnvioCarga.Guardada, null);

                pendientes.Remove(rutaId);
                await almacen.GuardarPendienteAsync(ClavePendientes, pendientes);

                if (respuesta.IsSuccessStatusCode)
                    return (ResultadoEnvioCarga.Enviada, null);

                string? mensaje = null;

                try
                {
                    mensaje = (await respuesta.Content.ReadFromJsonAsync<RespuestaError>())?.Mensaje;
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
                {
                }

                return (ResultadoEnvioCarga.Rechazada, mensaje ?? "El servidor no aceptó la carga.");
            }
        }
        finally
        {
            enviando.Release();
        }
    }

    private async Task<Dictionary<int, VerificarCargaRequest>> LeerPendientesAsync() =>
        await almacen.LeerPendienteAsync<Dictionary<int, VerificarCargaRequest>>(ClavePendientes) ?? [];

    private sealed record RespuestaError(string? Mensaje);
}
