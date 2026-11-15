using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Deposito;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

public class RendicionService(HttpClient http, AlmacenLocal almacen, ColaOperacionesService cola, TimeProvider reloj)
{
    private const string ClavePendientes = "driver:rendiciones-pendientes";

    private readonly SemaphoreSlim enviando = new(1, 1);

    public static List<(ParadaDto Parada, BultoRutaDto Bulto)> BultosADevolver(
        RutaDetalleDto ruta,
        IReadOnlyDictionary<int, OperacionRequest> pendientes)
    {
        var resultado = new List<(ParadaDto, BultoRutaDto)>();

        foreach (var parada in ruta.Paradas.OrderBy(p => p.Orden))
        {
            var bultos = (parada.Bultos ?? []).Where(ResultadoPorBulto.PorEntregar);

            if (pendientes.TryGetValue(parada.EnvioId, out var operacion))
            {
                var entregados = operacion.Tipo == "Entrega"
                    ? bultos.Select(b => b.Id).ToHashSet()
                    : (operacion.ResultadosBultos ?? []).Where(r => r.Entregado).Select(r => r.BultoId).ToHashSet();

                bultos = bultos.Where(b => !entregados.Contains(b.Id));
            }
            else if (parada.EstadoEnvio is "Entregado" or "Devuelto" or "Extraviado")
            {
                continue;
            }

            resultado.AddRange(bultos.Select(b => (parada, b)));
        }

        return resultado;
    }

    public async Task<bool> TienePendienteAsync(int rutaId) =>
        (await LeerPendientesAsync()).ContainsKey(rutaId);

    public async Task<(ResultadoEnvioCarga Resultado, string? Mensaje)> RendirAsync(
        int rutaId,
        IEnumerable<string> bultos,
        string? observaciones)
    {
        var pendientes = await LeerPendientesAsync();
        pendientes[rutaId] = new DeclararRendicionRequest(
            bultos.Select(c => c.ToUpperInvariant()).Order().ToList(),
            string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim(),
            reloj.GetUtcNow().UtcDateTime);
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

            if ((await cola.ObtenerPendientesAsync()).Any(o => o.RutaId == rutaId))
                return (ResultadoEnvioCarga.Guardada, null);

            HttpResponseMessage respuesta;

            try
            {
                respuesta = await http.PostAsJsonAsync($"api/rutas/mias/{rutaId}/rendicion", pedido);
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

                return (ResultadoEnvioCarga.Rechazada, mensaje ?? "El servidor no aceptó la rendición.");
            }
        }
        finally
        {
            enviando.Release();
        }
    }

    private async Task<Dictionary<int, DeclararRendicionRequest>> LeerPendientesAsync() =>
        await almacen.LeerPendienteAsync<Dictionary<int, DeclararRendicionRequest>>(ClavePendientes) ?? [];

    private sealed record RespuestaError(string? Mensaje);
}
