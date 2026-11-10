
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LastMileUY.API.Tests.Multitenancy;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AdministracionGlobalAutorizacionTests(
    BaseConDosOperadores baseDeDatos)
{
    private WebApplicationFactory<Program> CrearApi(string? rol)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:DefaultConnection",
                    baseDeDatos.CadenaAplicacion);

                builder.UseSetting("SeedAdmin:Enabled", "false");

                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme =
                            AutenticacionPrueba.Esquema;

                        options.DefaultChallengeScheme =
                            AutenticacionPrueba.Esquema;

                        options.DefaultForbidScheme =
                            AutenticacionPrueba.Esquema;
                    })
                    .AddScheme<
                        AuthenticationSchemeOptions,
                        AutenticacionPrueba>(
                        AutenticacionPrueba.Esquema, _ => { });

                    services.AddSingleton(
                        new RolDePrueba(rol));
                });
            });
    }

    [Theory]
    [InlineData("/api/admin/operadores")]
    [InlineData("/api/admin/comercios")]
    public async Task Sin_autenticar_devuelve_401(string ruta)
    {
        using var api = CrearApi(null);
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Unauthorized,
            respuesta.StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/operadores")]
    [InlineData("/api/admin/comercios")]
    public async Task Operador_devuelve_403(string ruta)
    {
        using var api = CrearApi("Operador");
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Forbidden,
            respuesta.StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/operadores")]
    [InlineData("/api/admin/comercios")]
    public async Task Administrador_global_puede_listar(string ruta)
    {
        using var api = CrearApi("Administrador");
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK,
            respuesta.StatusCode);
    }

    public sealed record RolDePrueba(string? Rol);

    public sealed class AutenticacionPrueba(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        RolDePrueba usuario)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        public const string Esquema = "AutenticacionPrueba";

        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            if (usuario.Rol is null)
            {
                return Task.FromResult(
                    AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier, "usuario-test"),
                new Claim(
                    ClaimTypes.Name, "Usuario de prueba"),
                new Claim(
                    ClaimTypes.Role, usuario.Rol)
            };

            var identidad = new ClaimsIdentity(
                claims, Esquema);

            var principal = new ClaimsPrincipal(identidad);

            var ticket = new AuthenticationTicket(
                principal, Esquema);

            return Task.FromResult(
                AuthenticateResult.Success(ticket));
        }
    }
}
