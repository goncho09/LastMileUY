using System.Net;
using LastMileUY.Contracts.Usuarios;

namespace LastMileUY.Backoffice.Servicios;

public sealed class UsuariosApiHttp(HttpClient http) : IUsuariosApi
{
    private const string Ruta = "api/usuarios";

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync(
        CancellationToken ct = default)
    {
        using var respuesta = await http.GetAsync(Ruta, ct);

        await ValidarRespuestaAsync(respuesta, ct);

        return await respuesta.Content
            .ReadFromJsonAsync<List<UsuarioDto>>(
                cancellationToken: ct) ?? [];
    }

    public async Task<UsuarioDto> CrearAsync(
        CrearUsuarioRequest request,
        CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync(
            Ruta, request, ct);

        await ValidarRespuestaAsync(respuesta, ct);

        return await respuesta.Content
            .ReadFromJsonAsync<UsuarioDto>(
                cancellationToken: ct)
            ?? throw new InvalidOperationException(
                "La API no devolvió el usuario creado.");
    }

    public async Task CambiarEstadoAsync(
        string usuarioId,
        bool activo,
        CancellationToken ct = default)
    {
        using var respuesta = await http.PatchAsJsonAsync(
            $"{Ruta}/{Uri.EscapeDataString(usuarioId)}/estado",
            new CambiarEstadoUsuarioRequest(activo),
            ct);

        await ValidarRespuestaAsync(respuesta, ct);
    }

    private static async Task ValidarRespuestaAsync(
        HttpResponseMessage respuesta,
        CancellationToken ct)
    {
        if (respuesta.StatusCode == HttpStatusCode.Forbidden)
            throw new OperacionRechazadaException(
                "No tenés permisos para gestionar usuarios.");

        if (respuesta.StatusCode == HttpStatusCode.BadRequest ||
            respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            throw new OperacionRechazadaException(
                await RespuestasApi.LeerMensajeAsync(
                    respuesta,
                    "La API rechazó la operación.",
                    ct));
        }

        respuesta.EnsureSuccessStatusCode();
    }
}
