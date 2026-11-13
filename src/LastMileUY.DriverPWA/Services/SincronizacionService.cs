using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

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
                }
                catch (TaskCanceledException)
                {
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

    public async Task<OperacionRequest?> ObtenerPendienteAsync(int envioId)
    {
        var pendientes = await cola.ObtenerPendientesAsync();

        return pendientes.FirstOrDefault(x => x.EnvioId == envioId);
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

    public async Task<EstadoEnvioOperacion> GuardarOperacionAsync(
        OperacionRequest operacion)
    {
        operacion = await cola.AgregarAsync(operacion);

        return await EnviarOperacionGuardadaAsync(operacion);
    }

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

    private readonly SemaphoreSlim sincronizando = new(1, 1);

    public async Task<(int Enviadas, int Pendientes)> SincronizarPendientesAsync()
    {
        if (!await sincronizando.WaitAsync(0))
            return (0, (await cola.ObtenerPendientesAsync()).Count);

        try
        {
            var operaciones = await cola.ObtenerPendientesAsync();
            var enviadas = 0;

            foreach (var operacion in operaciones.OrderBy(x => x.FechaHoraDispositivo))
            {
                var resultado = await ReintentarOperacionAsync(operacion.IdOperacion);

                if (!resultado.Pendiente)
                    enviadas++;
            }

            var restantes = await cola.ObtenerPendientesAsync();

            return (enviadas, restantes.Count);
        }
        finally
        {
            sincronizando.Release();
        }
    }
}
