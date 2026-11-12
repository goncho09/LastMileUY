using System.Security.Claims;
using System.Text.Encodings.Web;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LastMileUY.API.Modules.Integraciones.Application;

// Autenticación de la API pública: la clave viaja en el encabezado X-Api-Key.
// Solo la usan los endpoints de /api/v1 (los nombran en su política); el resto de la API sigue con la
// cookie de sesión, así que una clave no sirve para nada fuera de la API pública.
// La identidad que arma es la de un comercio con su operador ya elegido: los mismos claims que la
// sesión del portal, así que el filtro de EF, el RLS y ValidarVinculoComercioMiddleware se aplican igual.
public sealed class AutenticacionClaveApi(
    IOptionsMonitor<AuthenticationSchemeOptions> opciones,
    ILoggerFactory registro,
    UrlEncoder codificador,
    ApplicationDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, registro, codificador)
{
    public const string Esquema = "ClaveApi";
    public const string Encabezado = "X-Api-Key";

    // Qué clave hizo el pedido: separa los topes de pedidos y marca el canal de los envíos (Api)
    public const string ClaimClaveApiId = "clave_api_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Encabezado, out var valores) || string.IsNullOrWhiteSpace(valores))
            return AuthenticateResult.NoResult();

        var clave = valores.ToString().Trim();

        // Formato: así una clave pegada mal no llega a la base
        if (!clave.StartsWith(ClavesApiService.Inicio, StringComparison.Ordinal) || clave.Length > 100)
            return AuthenticateResult.Fail("Clave de API inválida.");

        var guardada = await ClavesApiService.BuscarParaAutenticarAsync(
            db, ClavesApiService.CalcularHash(clave), Context.RequestAborted);

        // Inexistente y revocada responden igual: 401
        if (guardada is null)
            return AuthenticateResult.Fail("Clave de API inválida o revocada.");

        var identidad = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"clave-api:{guardada.Id}"),
            new Claim(ClaimTypes.Name, $"Clave de API «{guardada.Nombre}»"),
            new Claim(ClaimTypes.Role, "Comercio"),
            new Claim(ContextoOperadorHttp.ClaimOperadorId, guardada.OperadorId.ToString()),
            new Claim(ContextoOperadorHttp.ClaimComercioId, guardada.ComercioId.ToString()),
            new Claim(ClaimClaveApiId, guardada.Id.ToString())
        ], Esquema);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema));
    }

    // Sin clave, o con una que no sirve: 401 con el esquema esperado, para que el integrador sepa qué falta
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{Esquema} header=\"{Encabezado}\"";
        return Response.WriteAsJsonAsync(new
        {
            mensaje = $"Falta una clave de API válida en el encabezado {Encabezado}."
        });
    }
}
