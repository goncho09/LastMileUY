using System.Security.Claims;
using System.Text.Encodings.Web;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LastMileUY.API.Modules.Integraciones.Application;

public sealed class AutenticacionClaveApi(
    IOptionsMonitor<AuthenticationSchemeOptions> opciones,
    ILoggerFactory registro,
    UrlEncoder codificador,
    ApplicationDbContext db,
    AmbientePruebasService ambientePruebas,
    IntentosFallidosClaveApi intentosFallidos)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, registro, codificador)
{
    public const string Esquema = "ClaveApi";
    public const string Encabezado = "X-Api-Key";

    public const string ClaimClaveApiId = "clave_api_id";

    private TimeSpan? bloqueadaPor;

    private string Ip => Context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Encabezado, out var valores) || string.IsNullOrWhiteSpace(valores))
            return AuthenticateResult.NoResult();

        if (intentosFallidos.EstaBloqueada(Ip, out var espera))
        {
            bloqueadaPor = espera;
            return AuthenticateResult.Fail("Demasiadas claves de API inválidas desde esta dirección.");
        }

        var clave = valores.ToString().Trim();

        if (!clave.StartsWith(ClavesApiService.Inicio, StringComparison.Ordinal) || clave.Length > 100)
            return Fallo("Clave de API inválida.");

        var guardada = await ClavesApiService.BuscarParaAutenticarAsync(
            db, ClavesApiService.CalcularHash(clave), Context.RequestAborted);

        if (guardada is null)
            return Fallo("Clave de API inválida o revocada.");

        var operadorId = guardada.OperadorId;

        if (guardada.EsPrueba)
        {
            if (await ambientePruebas.OperadorIdAsync(Context.RequestAborted) is not { } operadorPruebas)
                return AuthenticateResult.Fail("El ambiente de pruebas no está disponible.");

            operadorId = operadorPruebas;
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, $"clave-api:{guardada.Id}"),
            new Claim(ClaimTypes.Name, $"Clave de API «{guardada.Nombre}»"),
            new Claim(ClaimTypes.Role, "Comercio"),
            new Claim(ContextoOperadorHttp.ClaimOperadorId, operadorId.ToString()),
            new Claim(ContextoOperadorHttp.ClaimComercioId, guardada.ComercioId.ToString()),
            new Claim(ClaimClaveApiId, guardada.Id.ToString())
        };

        if (guardada.EsPrueba)
            claims.Add(new Claim(ContextoOperadorHttp.ClaimAmbiente, ContextoOperadorHttp.AmbientePruebas));

        var identidad = new ClaimsIdentity(claims, Esquema);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema));
    }

    private AuthenticateResult Fallo(string motivo)
    {
        intentosFallidos.RegistrarFallo(Ip);
        return AuthenticateResult.Fail(motivo);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (bloqueadaPor is { } espera)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString();
            return Response.WriteAsJsonAsync(new
            {
                mensaje = "Demasiadas claves de API inválidas desde esta dirección: esperá un momento."
            });
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{Esquema} header=\"{Encabezado}\"";
        return Response.WriteAsJsonAsync(new
        {
            mensaje = $"Falta una clave de API válida en el encabezado {Encabezado}."
        });
    }
}
