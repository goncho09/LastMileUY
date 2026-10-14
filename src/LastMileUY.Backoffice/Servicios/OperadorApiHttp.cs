using System.Net.Http.Json;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// Llama a la API real. Si la sesión venció, ReenviarCookieApi lanza SesionVencidaException.
public class OperadorApiHttp(HttpClient http) : IOperadorApi
{
    public async Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<OperadorActualDto>("api/operador", ct)
        ?? throw new InvalidOperationException("La API no devolvió los datos del operador.");
}
