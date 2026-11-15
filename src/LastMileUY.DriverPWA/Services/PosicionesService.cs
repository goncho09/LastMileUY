using System.Net.Http.Json;
using LastMileUY.Contracts.Flota;

namespace LastMileUY.DriverPWA.Services;

public class PosicionesService(HttpClient http, AlmacenLocal almacen, TimeProvider reloj)
{
    private const string ClavePendientes = "driver:posiciones-pendientes";
    public const int MaximoGuardadas = 500;
    public static readonly TimeSpan CadaCuantoEnviar = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim enviando = new(1, 1);
    private DateTimeOffset ultimoEnvio = DateTimeOffset.MinValue;

    public async Task RegistrarAsync(int rutaId, decimal latitud, decimal longitud, DateTime fechaHora)
    {
        var pendientes = await LeerAsync();
        var lista = pendientes.TryGetValue(rutaId, out var existente) ? existente : [];

        lista.Add(new PosicionRequest(latitud, longitud, fechaHora.ToUniversalTime()));
        pendientes[rutaId] = lista.TakeLast(MaximoGuardadas).ToList();
        await almacen.GuardarPendienteAsync(ClavePendientes, pendientes);

        if (reloj.GetUtcNow() - ultimoEnvio >= CadaCuantoEnviar)
            await EnviarPendientesAsync();
    }

    public async Task<int> CantidadPendienteAsync() => (await LeerAsync()).Values.Sum(l => l.Count);

    public async Task EnviarPendientesAsync()
    {
        await enviando.WaitAsync();

        try
        {
            ultimoEnvio = reloj.GetUtcNow();
            var pendientes = await LeerAsync();

            foreach (var (rutaId, posiciones) in pendientes.ToList())
            {
                foreach (var lote in posiciones.Chunk(200).ToList())
                {
                    HttpResponseMessage respuesta;

                    try
                    {
                        respuesta = await http.PostAsJsonAsync($"api/rutas/mias/{rutaId}/posiciones",
                            new RegistrarPosicionesRequest(lote));
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        return;
                    }

                    using (respuesta)
                    {
                        if ((int)respuesta.StatusCode >= 500 || respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                            return;
                    }

                    var restantes = pendientes[rutaId].Skip(lote.Length).ToList();

                    if (restantes.Count == 0)
                        pendientes.Remove(rutaId);
                    else
                        pendientes[rutaId] = restantes;

                    await almacen.GuardarPendienteAsync(ClavePendientes, pendientes);
                }
            }
        }
        finally
        {
            enviando.Release();
        }
    }

    private async Task<Dictionary<int, List<PosicionRequest>>> LeerAsync() =>
        await almacen.LeerPendienteAsync<Dictionary<int, List<PosicionRequest>>>(ClavePendientes) ?? [];
}
