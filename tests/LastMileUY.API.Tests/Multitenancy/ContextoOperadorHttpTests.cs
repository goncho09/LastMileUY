namespace LastMileUY.API.Tests.Multitenancy;

using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Http;

public class ContextoOperadorHttpTests
{
    [Fact]
    public void Toma_el_operador_del_usuario_con_sesion()
    {
        var contexto = Crear(new Claim(ContextoOperadorHttp.ClaimOperadorId, "7"));

        Assert.Equal(7, contexto.OperadorId);
    }

    [Fact]
    public void Un_usuario_sin_operador_no_tiene_operador()
    {
        var contexto = Crear(new Claim(ClaimTypes.Name, "usuario de comercio"));

        Assert.Null(contexto.OperadorId);
    }

    [Fact]
    public void Un_valor_que_no_es_numero_no_se_toma()
    {
        var contexto = Crear(new Claim(ContextoOperadorHttp.ClaimOperadorId, "1 OR 1=1"));

        Assert.Null(contexto.OperadorId);
    }

    [Fact]
    public void Fuera_de_un_pedido_no_hay_operador()
    {
        var contexto = new ContextoOperadorHttp(new HttpContextAccessor());

        Assert.Null(contexto.OperadorId);
    }

    private static ContextoOperadorHttp Crear(params Claim[] claims)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Prueba"))
        };

        return new ContextoOperadorHttp(new HttpContextAccessor { HttpContext = http });
    }
}
