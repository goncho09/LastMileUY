namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;

// Arma una ruta por HTTP como lo hacen el operador y el despachador desde el Backoffice:
// envíos nuevos del comercio 1, pasados a depósito, en una ruta del repartidor 1.
// diasAdelante: cada prueba usa su propia fecha, porque el repartidor tiene una sola ruta abierta por día.
public static class RutasDePuntaAPunta
{
    // Devuelve el primer envío de la ruta; RutaConEnviosAsync, todos
    public static async Task<(int RutaId, int EnvioId)> RutaPlanificadaAsync(
        ApiDePuntaAPunta api, HttpClient operador, HttpClient despachador, int diasAdelante)
    {
        var (rutaId, envioIds) = await RutaConEnviosAsync(api, operador, despachador, diasAdelante, cantidadEnvios: 1);
        return (rutaId, envioIds[0]);
    }

    public static async Task<(int RutaId, int[] EnvioIds)> RutaConEnviosAsync(
        ApiDePuntaAPunta api, HttpClient operador, HttpClient despachador, int diasAdelante, int cantidadEnvios)
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var envioIds = new int[cantidadEnvios];

        for (var i = 0; i < cantidadEnvios; i++)
        {
            using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
            {
                ComercioId = api.IdComercio1,
                DestinatarioId = destinatario,
                DireccionId = direccion
            });
            envioIds[i] = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;

            using var deposito = await operador.PatchAsJsonAsync($"api/envios/{envioIds[i]}/estado",
                new CambiarEstadoRequest { Estado = "EnDeposito" });
            Assert.True(deposito.IsSuccessStatusCode);
        }

        var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;

        using var ruta = await despachador.PostAsJsonAsync("api/rutas", new CrearRutaRequest
        {
            Nombre = $"Ruta de prueba +{diasAdelante}",
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(diasAdelante),
            RepartidorId = opciones.Repartidores[0].Id,
            VehiculoId = opciones.Vehiculos[0].Id,
            EnvioIds = [.. envioIds]
        });
        Assert.Equal(HttpStatusCode.Created, ruta.StatusCode);

        return ((await ruta.Content.ReadFromJsonAsync<RutaDetalleDto>())!.Id, envioIds);
    }

    // La misma ruta, ya despachada: los envíos quedan en tránsito y el repartidor la puede descargar
    public static async Task<(int RutaId, int EnvioId)> RutaDespachadaAsync(
        ApiDePuntaAPunta api, HttpClient operador, HttpClient despachador, int diasAdelante)
    {
        var (rutaId, envioId) = await RutaPlanificadaAsync(api, operador, despachador, diasAdelante);
        await DespacharAsync(despachador, rutaId);

        return (rutaId, envioId);
    }

    public static async Task DespacharAsync(HttpClient despachador, int rutaId)
    {
        using var despacho = await despachador.PostAsync($"api/rutas/{rutaId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, despacho.StatusCode);
    }
}
