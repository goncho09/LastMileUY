using LastMileUY.Contracts.Administracion;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comercios;

namespace LastMileUY.Backoffice.Servicios;

public sealed class GestionComerciosApiHttp(HttpClient http)
    : IGestionComerciosApi
{
    private const string Ruta = "api/admin/comercios";

    public async Task<List<ComercioAdminDto>> ListarAsync() =>
        await http.GetFromJsonAsync<List<ComercioAdminDto>>(Ruta) ?? [];

    public async Task<ComercioAdminDto?> ObtenerAsync(int id)
    {
        using var r = await http.GetAsync($"{Ruta}/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            return null;
        await VerificarAsync(r);
        return await r.Content.ReadFromJsonAsync<ComercioAdminDto>();
    }

    public async Task<ComercioAdminDto> CrearAsync(
        CrearComercioConUsuarioRequest request)
    {
        using var r = await http.PostAsJsonAsync(Ruta, request);
        await VerificarAsync(r);
        return await r.Content.ReadFromJsonAsync<ComercioAdminDto>()
            ?? throw new InvalidOperationException("Respuesta vacía de la API.");
    }

    public async Task EditarAsync(
        int id, GuardarComercioAdminRequest request)
    {
        using var r = await http.PutAsJsonAsync($"{Ruta}/{id}", request);
        await VerificarAsync(r);
    }

    public async Task<List<VinculoOperadorComercioDto>> ListarVinculosAsync(
        int id) =>
        await http.GetFromJsonAsync<List<VinculoOperadorComercioDto>>(
            $"{Ruta}/{id}/operadores") ?? [];

    public async Task VincularAsync(int comercioId, int operadorId)
    {
        using var r = await http.PostAsync(
            $"{Ruta}/{comercioId}/operadores/{operadorId}", null);
        await VerificarAsync(r);
    }

    public async Task DesvincularAsync(int comercioId, int operadorId)
    {
        using var r = await http.DeleteAsync(
            $"{Ruta}/{comercioId}/operadores/{operadorId}");
        await VerificarAsync(r);
    }

    private static async Task VerificarAsync(HttpResponseMessage r)
    {
        if (r.StatusCode == HttpStatusCode.Forbidden)
            throw new OperacionRechazadaException(
                "No tenés permisos para administrar comercios.");

        if (r.StatusCode == HttpStatusCode.BadRequest)
            throw new OperacionRechazadaException(
                await RespuestasApi.LeerMensajeAsync(
                    r, "La API rechazó la operación.",
                    CancellationToken.None));

        r.EnsureSuccessStatusCode();
    }
}
