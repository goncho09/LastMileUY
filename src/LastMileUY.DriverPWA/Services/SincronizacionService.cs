using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

// Entregas e intentos fallidos: la app los manda por la misma API de sincronización que va a usar
// cuando registre sin conexión (POST /api/sincronizacion, ver docs/sincronizacion.md). Así la API
// controla la evidencia que pide la regla, descarta los reenvíos y el Backoffice ve cada operación.
public class SincronizacionService(HttpClient http,AlmacenLocal almacen)
{
    // El IdOperacion de lo que se está registrando para cada envío. Se conserva mientras no haya
    // un resultado final (se cortó la conexión o la API pidió reintentar): el reenvío lleva el mismo
    // id y la API no lo aplica dos veces. Con un resultado final, lo próximo es una operación nueva.
    private readonly Dictionary<int, Guid> enCurso = [];

    
    public async Task<List<MotivoNoEntregaDto>> ObtenerMotivosAsync()
    {
        const string clave = "driver:motivos";

        try
        {
            using var response = await http.GetAsync("api/sincronizacion/motivos");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Tu sesión expiró.");

            response.EnsureSuccessStatusCode();

            var motivos = await response.Content
                .ReadFromJsonAsync<List<MotivoNoEntregaDto>>() ?? [];

            await almacen.GuardarAsync(
                clave,
                motivos,
                AlmacenLocal.DuracionMaxima);

            return motivos;
        }
        catch (HttpRequestException)
        {
            return await almacen.LeerAsync<List<MotivoNoEntregaDto>>(clave) ?? [];
        }
        catch (TaskCanceledException)
        {
            return await almacen.LeerAsync<List<MotivoNoEntregaDto>>(clave) ?? [];
        }
    }
    
    // Lo que mandó este repartidor y qué pasó con cada cosa, lo más nuevo primero.
    // resultado: null (todas), "Aplicada", "Rechazada" o "Conflicto".
    public async Task<PaginaDto<OperacionSincronizadaDto>> ObtenerMisOperacionesAsync(string? resultado, int pagina)
    {
        var url = $"api/sincronizacion/mias?pagina={pagina}"
                  + (resultado is null ? "" : $"&resultado={Uri.EscapeDataString(resultado)}");

        using var response = await http.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Tu sesión expiró.");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PaginaDto<OperacionSincronizadaDto>>()
               ?? new PaginaDto<OperacionSincronizadaDto>([], 0, pagina, 20);
    }

    // Completa IdOperacion y la manda sola. Si no llega a la API, lanza HttpRequestException
    // y el próximo intento reusa el mismo IdOperacion.
    public async Task<ResultadoOperacionDto> EnviarAsync(OperacionRequest operacion)
    {
        if (!enCurso.TryGetValue(operacion.EnvioId, out var idOperacion))
        {
            idOperacion = Guid.NewGuid();
            enCurso[operacion.EnvioId] = idOperacion;
        }

        operacion.IdOperacion = idOperacion;

        using var response = await http.PostAsJsonAsync(
            "api/sincronizacion",
            new SincronizarRequest { Operaciones = [operacion] });

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Tu sesión expiró.");

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Tu usuario no puede registrar entregas.");

        response.EnsureSuccessStatusCode();

        var respuesta = await response.Content.ReadFromJsonAsync<SincronizarResponse>();
        var resultado = respuesta?.Resultados.SingleOrDefault()
                        ?? throw new HttpRequestException("La API no devolvió el resultado de la operación.");

        if (!resultado.Reintentar)
        {
            enCurso.Remove(operacion.EnvioId);
        }

        return resultado;
    }
}
