using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace LastMileUY.API.Tests.Envios;

using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Tests.Multitenancy;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Listado y detalle de envíos que consume el Backoffice, contra la base real con RLS
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ConsultasEnviosTests(BaseConDosOperadores baseDeDatos)
{
    private static IHttpContextAccessor CrearHttpContextAccessor()
    {
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "usuario-test"),
                        new Claim(ClaimTypes.Role, "Operador")
                    ], "Test"))
            }
        };
    }
    
    [Fact]
    public async Task El_listado_solo_trae_envios_del_operador()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorB);

        var pagina = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ListarAsync(1, 100, null, null);

        Assert.Contains(pagina.Items, e => e.Id == EnvioDeB);
        Assert.DoesNotContain(pagina.Items, e => e.Id == EnvioDeA);
        Assert.Equal(pagina.Items.Count, pagina.Total);
    }

    [Fact]
    public async Task El_listado_trae_los_datos_de_la_fila()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var pagina = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ListarAsync(1, 20, null, "CODIGOA");

        var fila = Assert.Single(pagina.Items);
        Assert.Equal(EnvioDeA, fila.Id);
        Assert.Equal("Comercio", fila.Comercio);
        Assert.Equal("Ana Silva", fila.Destinatario);
        Assert.Equal("Montevideo", fila.Ciudad);
        Assert.Equal("Admitido", fila.Estado);
        Assert.Equal("Estandar", fila.Modalidad);
        Assert.Equal(1, fila.CantidadBultos);
    }

    [Theory]
    [InlineData("codigoa")]
    [InlineData("ana sil")]
    public async Task Se_busca_por_codigo_o_destinatario_sin_importar_mayusculas(string texto)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var pagina = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ListarAsync(1, 100, null, texto);

        Assert.Contains(pagina.Items, e => e.Id == EnvioDeA);
    }

    [Fact]
    public async Task Un_porcentaje_en_la_busqueda_no_es_un_comodin()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var pagina = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ListarAsync(1, 20, null, "%");

        Assert.Empty(pagina.Items);
    }

    [Fact]
    public async Task Se_filtra_por_estado()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorB);
        var consultas = new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor());

        var admitidos = await consultas.ListarAsync(1, 100, "admitido", null);
        var entregados = await consultas.ListarAsync(1, 100, "Entregado", null);

        Assert.Contains(admitidos.Items, e => e.Id == EnvioDeB);
        Assert.DoesNotContain(entregados.Items, e => e.Id == EnvioDeB);
    }

    [Fact]
    public async Task Un_estado_que_no_existe_es_un_error()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ListarAsync(1, 20, "Volando", null));
    }

    [Fact]
    public async Task La_pagina_y_su_tamanio_se_acotan()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var pagina = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ListarAsync(0, 5000, null, null);

        Assert.Equal(1, pagina.Pagina);
        Assert.Equal(ConsultasEnviosService.TamanioPaginaMaximo, pagina.TamanioPagina);
    }

    [Fact]
    public async Task El_detalle_trae_destinatario_direccion_y_bultos()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var envio = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ObtenerDetalleAsync(EnvioDeA);

        Assert.NotNull(envio);
        Assert.Equal("CODIGOA", envio.CodigoSeguimiento);
        Assert.Equal("Ana", envio.Destinatario.Nombre);
        Assert.Equal("Rivera", envio.Direccion.Calle);
        Assert.Equal("BA1", Assert.Single(envio.Bultos).Codigo);
    }

    [Fact]
    public async Task El_detalle_de_un_envio_ajeno_no_se_encuentra()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var envio = await new ConsultasEnviosService(db,new ContextoOperadorFijo(OperadorB),  CrearHttpContextAccessor()).ObtenerDetalleAsync(EnvioDeB);

        Assert.Null(envio);
    }
}
