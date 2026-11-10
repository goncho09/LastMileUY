using LastMileUY.Contracts.Administracion;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public sealed class GestionOperadoresApiHttp(HttpClient http)
    : IGestionOperadoresApi
{
    private const string Ruta = "api/admin/operadores";

    public async Task<List<OperadorAdminDto>> ListarAsync()
    {
        return await http.GetFromJsonAsync<List<OperadorAdminDto>>(Ruta)
               ?? [];
    }

    public async Task<OperadorAdminDto?> ObtenerAsync(int id)
    {
        using var respuesta = await http.GetAsync($"{Ruta}/{id}");

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            return null;

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content
            .ReadFromJsonAsync<OperadorAdminDto>();
    }

    public async Task<OperadorAdminDto> CrearAsync(
        CrearOperadorConUsuarioRequest request)
    {
        using var respuesta = await http.PostAsJsonAsync(Ruta, request);

        await VerificarRespuestaAsync(respuesta);

        return await respuesta.Content
            .ReadFromJsonAsync<OperadorAdminDto>()
            ?? throw new InvalidOperationException(
                "La API no devolvió el operador creado.");
    }

    public async Task EditarAsync(
        int id,
        GuardarOperadorAdminRequest request)
    {
        using var respuesta = await http.PutAsJsonAsync(
            $"{Ruta}/{id}", request);

        await VerificarRespuestaAsync(respuesta);
    }

    private static async Task VerificarRespuestaAsync(
        HttpResponseMessage respuesta)
    {
        if (respuesta.StatusCode == HttpStatusCode.Forbidden)
            throw new OperacionRechazadaException(
                "No tenés permisos para administrar operadores.");

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            throw new OperacionRechazadaException(
                await RespuestasApi.LeerMensajeAsync(
                    respuesta,
                    "Los datos del operador no son válidos.",
                    CancellationToken.None));

        respuesta.EnsureSuccessStatusCode();
    }
}
