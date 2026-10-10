namespace LastMileUY.API.Tests.Envios;

using LastMileUY.API.Modules.Envios.Domain;

public class TransicionesEnvioTests
{
    [Theory]
    [InlineData(EstadoEnvio.Admitido, EstadoEnvio.EnDeposito)]
    [InlineData(EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta)]
    [InlineData(EstadoEnvio.AsignadoARuta, EstadoEnvio.EnDeposito)]
    [InlineData(EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito)]
    [InlineData(EstadoEnvio.EnTransito, EstadoEnvio.Entregado)]
    [InlineData(EstadoEnvio.EnTransito, EstadoEnvio.NoEntregado)]
    [InlineData(EstadoEnvio.EnTransito, EstadoEnvio.EnDeposito)]
    [InlineData(EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado)]
    [InlineData(EstadoEnvio.NoEntregado, EstadoEnvio.EnDevolucion)]
    [InlineData(EstadoEnvio.Reprogramado, EstadoEnvio.AsignadoARuta)]
    [InlineData(EstadoEnvio.Entregado, EstadoEnvio.EnDevolucion)]
    [InlineData(EstadoEnvio.EnDevolucion, EstadoEnvio.Devuelto)]
    [InlineData(EstadoEnvio.EnDevolucion, EstadoEnvio.Extraviado)]
    public void Transicion_de_la_tabla_es_valida(EstadoEnvio desde, EstadoEnvio hacia)
    {
        Assert.True(TransicionesEnvio.EsValida(desde, hacia));
    }

    [Theory]
    [InlineData(EstadoEnvio.Admitido, EstadoEnvio.Entregado)]
    [InlineData(EstadoEnvio.Admitido, EstadoEnvio.EnTransito)]
    [InlineData(EstadoEnvio.Entregado, EstadoEnvio.EnTransito)]
    [InlineData(EstadoEnvio.Devuelto, EstadoEnvio.EnDeposito)]
    [InlineData(EstadoEnvio.Extraviado, EstadoEnvio.EnDeposito)]
    [InlineData(EstadoEnvio.Reprogramado, EstadoEnvio.Entregado)]
    public void Transicion_fuera_de_la_tabla_se_rechaza(EstadoEnvio desde, EstadoEnvio hacia)
    {
        Assert.False(TransicionesEnvio.EsValida(desde, hacia));
    }

    [Fact]
    public void Envio_nuevo_arranca_admitido()
    {
        var envio = new Envio();

        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
    }

    [Fact]
    public void CambiarEstado_aplica_una_transicion_valida()
    {
        var envio = new Envio();

        envio.CambiarEstado(EstadoEnvio.EnDeposito);

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
    }

    [Fact]
    public void CambiarEstado_rechaza_una_transicion_invalida_y_no_cambia_el_estado()
    {
        var envio = new Envio();

        Assert.Throws<InvalidOperationException>(() => envio.CambiarEstado(EstadoEnvio.Entregado));
        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
    }

    [Fact]
    public void Cada_envio_tiene_un_codigo_de_seguimiento_distinto()
    {
        var codigos = Enumerable.Range(0, 1000).Select(_ => new Envio().CodigoSeguimiento).ToList();

        Assert.All(codigos, c => Assert.Equal(12, c.Length));
        Assert.Equal(codigos.Count, codigos.Distinct().Count());
    }
}
