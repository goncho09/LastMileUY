using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

// Entregas e intentos fallidos: la app los manda por la API de sincronización
// (POST /api/sincronizacion, ver docs/sincronizacion.md). Así la API controla la evidencia que pide
// la regla, descarta los reenvíos y el Backoffice ve cada operación.
// Cada operación se guarda primero en la cola del celular (ColaOperacionesService) con su
// IdOperacion: si no llega a la API, queda ahí y el reenvío lleva el mismo id, para que la API no la
// aplique dos veces. Con un resultado final sale de la cola.
public class SincronizacionService(
    HttpClient http,
    AlmacenLocal almacen,
    RutasService rutasService,
    OperacionesConfirmadasService confirmadas,
    ColaOperacionesService cola)
{
    private async Task<EstadoEnvioOperacion> EnviarOperacionGuardadaAsync(
        OperacionRequest operacion)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "api/sincronizacion",
                new SincronizarRequest
                {
                    Operaciones = [operacion]
                });

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Tu sesión expiró.");

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException(
                    "Tu usuario no puede registrar entregas.");

            response.EnsureSuccessStatusCode();

            var respuesta = await response.Content
                .ReadFromJsonAsync<SincronizarResponse>();

            var resultado = respuesta?.Resultados.SingleOrDefault()
                            ?? throw new HttpRequestException(
                                "La API no devolvió el resultado de la operación.");

            if (!resultado.Reintentar)
            {
                // Solo bloqueamos permanentemente las entregas exitosas.
                // Los intentos fallidos no se guardan como entregas confirmadas.
                if (resultado.Resultado == "Aplicada" &&
                    operacion.Tipo == "Entrega")
                {
                    await confirmadas.RegistrarAsync(operacion.EnvioId);
                }

                await cola.EliminarAsync(operacion.IdOperacion);

                try
                {
                    await rutasService.ActualizarCacheRutaAsync(operacion.RutaId);
                }
                catch (HttpRequestException)
                {
                    // El resultado de la operación ya es definitivo.
                }
                catch (TaskCanceledException)
                {
                    // Conservamos la caché anterior.
                }
            }

            return new EstadoEnvioOperacion(
                resultado.Reintentar,
                resultado);
        }
        catch (HttpRequestException)
        {
            return new EstadoEnvioOperacion(true, null);
        }
        catch (TaskCanceledException)
        {
            return new EstadoEnvioOperacion(true, null);
        }
    }

    public async Task<bool> TieneOperacionPendienteAsync(int envioId)
    {
        var pendientes = await cola.ObtenerPendientesAsync();

        return pendientes.Any(x => x.EnvioId == envioId);
    }
    
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

    // Guarda una operación localmente antes de intentar sincronizarla.
    // Si no hay conexión, permanece en la cola para enviarla después.
    public async Task<EstadoEnvioOperacion> GuardarOperacionAsync(
        OperacionRequest operacion)
    {
        // La operación se guarda antes de intentar enviarla.
        operacion = await cola.AgregarAsync(operacion);

        return await EnviarOperacionGuardadaAsync(operacion);
    }
    
    // Reintenta una operación existente sin generar otro IdOperacion.
    public async Task<EstadoEnvioOperacion> ReintentarOperacionAsync(
        Guid idOperacion)
    {
        var pendientes = await cola.ObtenerPendientesAsync();

        var operacion = pendientes.FirstOrDefault(x =>
            x.IdOperacion == idOperacion);

        if (operacion is null)
            throw new InvalidOperationException(
                "La operación ya no está pendiente.");

        return await EnviarOperacionGuardadaAsync(operacion);
    }
    
    public async Task<(int Enviadas, int Pendientes)> SincronizarPendientesAsync()
    {
        var operaciones = await cola.ObtenerPendientesAsync();
        var enviadas = 0;

        foreach (var operacion in operaciones.OrderBy(x => x.FechaHoraDispositivo))
        {
            var resultado = await ReintentarOperacionAsync(operacion.IdOperacion);

            // Si la API dio un resultado definitivo, la operación
            // ya fue eliminada de la cola por EnviarOperacionGuardadaAsync.
            if (!resultado.Pendiente)
                enviadas++;
        }

        var restantes = await cola.ObtenerPendientesAsync();

        return (enviadas, restantes.Count);
    }
}
