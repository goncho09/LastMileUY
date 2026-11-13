
using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Tests.Multitenancy;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ValidarVinculoComercioMiddlewareTests(
    BaseConDosOperadores baseDeDatos)
{
    private static DefaultHttpContext CrearHttpContext(
        int? operadorId,
        int? comercioId,
        string ruta = "/api/envios")
    {
        var claims = new List<Claim>();

        if (operadorId.HasValue)
        {
            claims.Add(new Claim(
                ContextoOperadorHttp.ClaimOperadorId,
                operadorId.Value.ToString()));
        }

        if (comercioId.HasValue)
        {
            claims.Add(new Claim(
                ContextoOperadorHttp.ClaimComercioId,
                comercioId.Value.ToString()));
        }

        claims.Add(new Claim(ClaimTypes.NameIdentifier, "usuario-test"));

        var identidad = new ClaimsIdentity(claims, "Test");
        var contexto = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identidad)
        };

        contexto.Request.Path = ruta;
        contexto.Response.Body = new MemoryStream();

        return contexto;
    }

    private async Task<(int Estado, bool Continuo)> EjecutarAsync(
        int? operadorId,
        int? comercioId,
        string ruta = "/api/envios")
    {
        var continuo = false;

        var middleware = new ValidarVinculoComercioMiddleware(
            _ =>
            {
                continuo = true;
                return Task.CompletedTask;
            });

        var http = CrearHttpContext(operadorId, comercioId, ruta);

        await using var db = baseDeDatos.CrearContexto(
            operadorId, comercioId);

        await middleware.InvokeAsync(http, db);

        return (http.Response.StatusCode, continuo);
    }

    [Fact]
    public async Task Vinculo_activo_permite_continuar()
    {
        var resultado = await EjecutarAsync(
            BaseConDosOperadores.OperadorA,
            BaseConDosOperadores.ComercioCompartido);

        Assert.True(resultado.Continuo);
        Assert.Equal(StatusCodes.Status200OK, resultado.Estado);
    }

    [Fact]
    public async Task Comercio_sin_vinculo_devuelve_403()
    {
        var resultado = await EjecutarAsync(
            BaseConDosOperadores.OperadorA,
            BaseConDosOperadores.ComercioSoloDeB);

        Assert.False(resultado.Continuo);
        Assert.Equal(StatusCodes.Status403Forbidden, resultado.Estado);
    }

    [Fact]
    public async Task Comercio_compartido_funciona_con_ambos_operadores()
    {
        var resultadoA = await EjecutarAsync(
            BaseConDosOperadores.OperadorA,
            BaseConDosOperadores.ComercioCompartido);

        var resultadoB = await EjecutarAsync(
            BaseConDosOperadores.OperadorB,
            BaseConDosOperadores.ComercioCompartido);

        Assert.True(resultadoA.Continuo);
        Assert.True(resultadoB.Continuo);
    }

    [Fact]
    public async Task Usuario_de_operador_sin_comercio_puede_continuar()
    {
        var resultado = await EjecutarAsync(
            BaseConDosOperadores.OperadorA,
            null);

        Assert.True(resultado.Continuo);
    }

    [Fact]
    public async Task Comercio_sin_operador_devuelve_403()
    {
        var resultado = await EjecutarAsync(
            null,
            BaseConDosOperadores.ComercioCompartido);

        Assert.False(resultado.Continuo);
        Assert.Equal(StatusCodes.Status403Forbidden, resultado.Estado);
    }

    [Fact]
    public async Task Login_y_logout_permiten_continuar_sin_vinculo()
    {
        var login = await EjecutarAsync(
            BaseConDosOperadores.OperadorA,
            BaseConDosOperadores.ComercioSoloDeB,
            "/api/auth/login");

        var logout = await EjecutarAsync(
            BaseConDosOperadores.OperadorA,
            BaseConDosOperadores.ComercioSoloDeB,
            "/api/auth/logout");

        Assert.True(login.Continuo);
        Assert.True(logout.Continuo);
    }

    [Fact]
    public async Task Vinculo_desactivado_bloquea_solo_al_operador_afectado()
    {
        const int operadorA = BaseConDosOperadores.OperadorA;
        const int operadorB = BaseConDosOperadores.OperadorB;
        const int comercio = BaseConDosOperadores.ComercioCompartido;

        var opciones = new DbContextOptionsBuilder<
            LastMileUY.API.Infrastructure.Persistence.ApplicationDbContext>()
            .UseNpgsql(baseDeDatos.CadenaAdministrador)
            .Options;

        await using var admin =
            new LastMileUY.API.Infrastructure.Persistence.ApplicationDbContext(
                opciones,
                new ContextoOperadorFijo(null));

        try
        {
            var vinculo = await admin.OperadoresComercios
                .SingleAsync(v =>
                    v.OperadorId == operadorA &&
                    v.ComercioId == comercio);

            vinculo.Activo = false;
            await admin.SaveChangesAsync();

            var resultadoA = await EjecutarAsync(operadorA, comercio);
            var resultadoB = await EjecutarAsync(operadorB, comercio);

            Assert.False(resultadoA.Continuo);
            Assert.Equal(StatusCodes.Status403Forbidden, resultadoA.Estado);

            Assert.True(resultadoB.Continuo);
            Assert.Equal(StatusCodes.Status200OK, resultadoB.Estado);
        }
        finally
        {
            admin.ChangeTracker.Clear();

            var vinculo = await admin.OperadoresComercios
                .SingleAsync(v =>
                    v.OperadorId == operadorA &&
                    v.ComercioId == comercio);

            vinculo.Activo = true;
            await admin.SaveChangesAsync();
        }
    }
}
