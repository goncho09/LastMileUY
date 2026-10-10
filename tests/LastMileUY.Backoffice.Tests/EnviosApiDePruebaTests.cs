namespace LastMileUY.Backoffice.Tests;

using LastMileUY.Backoffice.Servicios;

public class EnviosApiDePruebaTests
{
    private readonly EnviosApiDePrueba api = new();

    [Fact]
    public async Task Sin_filtro_devuelve_la_primera_pagina_y_el_total()
    {
        var resultado = await api.ListarAsync(new FiltroEnvios(TamanioPagina: 10));

        Assert.Equal(10, resultado.Items.Count);
        Assert.Equal(45, resultado.Total);
        Assert.Equal(5, resultado.TotalPaginas);
    }

    [Fact]
    public async Task La_ultima_pagina_trae_lo_que_sobra()
    {
        var resultado = await api.ListarAsync(new FiltroEnvios(Pagina: 5, TamanioPagina: 10));

        Assert.Equal(5, resultado.Items.Count);
    }

    [Fact]
    public async Task Filtrar_por_estado_solo_trae_ese_estado()
    {
        var resultado = await api.ListarAsync(new FiltroEnvios(Estado: "Entregado", TamanioPagina: 100));

        Assert.NotEmpty(resultado.Items);
        Assert.All(resultado.Items, e => Assert.Equal("Entregado", e.Estado));
    }

    [Fact]
    public async Task Buscar_por_referencia_encuentra_el_envio()
    {
        var resultado = await api.ListarAsync(new FiltroEnvios(Buscar: "ped-1001"));

        var envio = Assert.Single(resultado.Items);
        Assert.Equal("PED-1001", envio.ReferenciaExterna);
    }

    [Fact]
    public async Task El_listado_viene_del_mas_nuevo_al_mas_viejo()
    {
        var resultado = await api.ListarAsync(new FiltroEnvios(TamanioPagina: 100));

        Assert.Equal(
            resultado.Items.OrderByDescending(e => e.FechaCreacion).Select(e => e.Id),
            resultado.Items.Select(e => e.Id));
    }

    [Fact]
    public async Task Obtener_un_envio_existente_trae_bultos_y_historial()
    {
        var envio = await api.ObtenerAsync(1);

        Assert.NotNull(envio);
        Assert.NotEmpty(envio.Bultos);
        Assert.Equal(envio.Estado, envio.Eventos[^1].Tipo);
    }

    [Fact]
    public async Task Obtener_un_envio_que_no_existe_devuelve_null()
    {
        Assert.Null(await api.ObtenerAsync(9999));
    }
}
