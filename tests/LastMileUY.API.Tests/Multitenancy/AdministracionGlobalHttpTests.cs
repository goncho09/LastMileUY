using LastMileUY.Contracts.Administracion;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LastMileUY.Contracts.Comercios;
using LastMileUY.Contracts.Operadores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LastMileUY.API.Tests.Multitenancy;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AdministracionGlobalHttpTests(
    BaseConDosOperadores baseDeDatos)
{
    private WebApplicationFactory<Program> CrearApi()
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
                            AutenticacionAdmin.Esquema;

                        options.DefaultChallengeScheme =
                            AutenticacionAdmin.Esquema;

                        options.DefaultForbidScheme =
                            AutenticacionAdmin.Esquema;
                    })
                    .AddScheme<
                        AuthenticationSchemeOptions,
                        AutenticacionAdmin>(
                        AutenticacionAdmin.Esquema, _ => { });
                });
            });
    }

    [Fact]
    public async Task Flujo_completo_administracion_global()
    {
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var crearOperador = await cliente.PostAsJsonAsync(
            "/api/admin/operadores",
            new CrearOperadorConUsuarioRequest(
                $"Operador HTTP {Guid.NewGuid():N}",
                "#336699", null,
                new UsuarioInicialRequest("Responsable", "Operador", $"op-{Guid.NewGuid():N}@ejemplo.com", "ClaveInicial123")));

        Assert.Equal(
            HttpStatusCode.Created,
            crearOperador.StatusCode);

        var operador = await crearOperador.Content
            .ReadFromJsonAsync<OperadorAdminDto>();

        Assert.NotNull(operador);

        var editarOperador = await cliente.PutAsJsonAsync(
            $"/api/admin/operadores/{operador.Id}",
            new GuardarOperadorAdminRequest(
                "Operador HTTP Editado",
                "#112233",
                null));

        Assert.Equal(
            HttpStatusCode.NoContent,
            editarOperador.StatusCode);

        var crearComercio = await cliente.PostAsJsonAsync(
            "/api/admin/comercios",
            new CrearComercioConUsuarioRequest(
                $"Comercio HTTP {Guid.NewGuid():N}",
                "Razón Social HTTP", null,
                "http@ejemplo.com", "099123456",
                new UsuarioInicialRequest("Responsable", "Comercio", $"com-{Guid.NewGuid():N}@ejemplo.com", "ClaveInicial123")));

        Assert.Equal(
            HttpStatusCode.Created,
            crearComercio.StatusCode);

        var comercio = await crearComercio.Content
            .ReadFromJsonAsync<ComercioAdminDto>();

        Assert.NotNull(comercio);

        var editarComercio = await cliente.PutAsJsonAsync(
            $"/api/admin/comercios/{comercio.Id}",
            new GuardarComercioAdminRequest(
                "Comercio HTTP Editado",
                "Nueva Razón Social",
                null,
                "editado@ejemplo.com",
                "098123456"));

        Assert.Equal(
            HttpStatusCode.NoContent,
            editarComercio.StatusCode);

        var rutaVinculo =
            $"/api/admin/comercios/{comercio.Id}/operadores/{operador.Id}";

        var vincular = await cliente.PostAsync(
            rutaVinculo, null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            vincular.StatusCode);

        await VerificarVinculoAsync(
            comercio.Id, operador.Id, true);

        var duplicado = await cliente.PostAsync(
            rutaVinculo, null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            duplicado.StatusCode);

        await VerificarVinculoAsync(
            comercio.Id, operador.Id, true);

        var desvincular = await cliente.DeleteAsync(
            rutaVinculo);

        Assert.Equal(
            HttpStatusCode.NoContent,
            desvincular.StatusCode);

        await VerificarVinculoAsync(
            comercio.Id, operador.Id, false);

        var revincular = await cliente.PostAsync(
            rutaVinculo, null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            revincular.StatusCode);

        await VerificarVinculoAsync(
            comercio.Id, operador.Id, true);

        var operadorFinal = await cliente.GetFromJsonAsync<
            OperadorAdminDto>(
            $"/api/admin/operadores/{operador.Id}");

        var comercioFinal = await cliente.GetFromJsonAsync<
            ComercioAdminDto>(
            $"/api/admin/comercios/{comercio.Id}");

        Assert.NotNull(operadorFinal);
        Assert.NotNull(comercioFinal);

        Assert.Equal(
            "Operador HTTP Editado",
            operadorFinal.Nombre);

        Assert.Equal(
            "#112233",
            operadorFinal.ColorPrimario);

        Assert.Equal(
            "Comercio HTTP Editado",
            comercioFinal.Nombre);

        Assert.Equal(
            "editado@ejemplo.com",
            comercioFinal.Email);
    }

    private async Task VerificarVinculoAsync(
        int comercioId,
        int operadorId,
        bool activo)
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var vinculos = await db.OperadoresComercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v =>
                v.ComercioId == comercioId &&
                v.OperadorId == operadorId)
            .ToListAsync();

        var vinculo = Assert.Single(vinculos);

        Assert.Equal(activo, vinculo.Activo);
    }

    public sealed class AutenticacionAdmin(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        public const string Esquema = "AdminHttpPrueba";

        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    "administrador-test"),
                new Claim(
                    ClaimTypes.Name,
                    "Administrador de prueba"),
                new Claim(
                    ClaimTypes.Role,
                    "Administrador")
            };

            var identidad = new ClaimsIdentity(
                claims, Esquema);

            var principal = new ClaimsPrincipal(identidad);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal, Esquema)));
        }
    }
}
