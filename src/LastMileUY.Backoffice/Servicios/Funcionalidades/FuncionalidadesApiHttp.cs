using System.Net;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// GET /api/admin/funcionalidades y PUT /api/admin/operadores/{id}/funcionalidades/{clave}
public class FuncionalidadesApiHttp(HttpClient http) : IFuncionalidadesApi
{
    public async Task<IReadOnlyList<FuncionalidadesOperadorDto>> ListarAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<FuncionalidadesOperadorDto>>("api/admin/funcionalidades", ct)
        ?? throw new InvalidOperationException("La API no devolvió las funcionalidades.");

    public async Task CambiarAsync(int operadorId, string clave, bool activa, CancellationToken ct = default)
    {
        using var respuesta = await http.PutAsJsonAsync(
            $"api/admin/operadores/{operadorId}/funcionalidades/{Uri.EscapeDataString(clave)}",
            new CambiarFuncionalidadRequest { Activa = activa }, ct);

        switch (respuesta.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                throw new OperacionRechazadaException("Solo el administrador de la plataforma cambia las funcionalidades.");
            case HttpStatusCode.NotFound:
                throw new OperacionRechazadaException("Ese operador no existe.");
            case HttpStatusCode.BadRequest:
                throw new OperacionRechazadaException(
                    await RespuestasApi.LeerMensajeAsync(respuesta, "La API no aceptó el cambio.", ct));
        }

        respuesta.EnsureSuccessStatusCode();
    }
}
