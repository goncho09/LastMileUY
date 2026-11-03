using LastMileUY.API.Modules.Envios.Domain;
using Xunit;

public class BultoTests
{
    [Fact]
    public void BultoNuevo_ComienzaPendiente()
    {
        var bulto = new Bulto();

        Assert.Equal(EstadoBulto.Pendiente, bulto.Estado);
    }

    [Theory]
    [InlineData(EstadoBulto.Entregado)]
    [InlineData(EstadoBulto.NoEntregado)]
    [InlineData(EstadoBulto.Devuelto)]
    [InlineData(EstadoBulto.Extraviado)]
    public void BultoPendiente_PermiteCambiarAEstadoValido(
        EstadoBulto nuevoEstado)
    {
        var bulto = new Bulto();

        bulto.CambiarEstado(nuevoEstado);

        Assert.Equal(nuevoEstado, bulto.Estado);
    }

    [Fact]
    public void BultoNoEntregado_PuedeVolverAPendiente()
    {
        var bulto = new Bulto();

        bulto.CambiarEstado(EstadoBulto.NoEntregado);
        bulto.CambiarEstado(EstadoBulto.Pendiente);

        Assert.Equal(EstadoBulto.Pendiente, bulto.Estado);
    }

    [Theory]
    [InlineData(EstadoBulto.Entregado)]
    [InlineData(EstadoBulto.Devuelto)]
    [InlineData(EstadoBulto.Extraviado)]
    public void BultoFinalizado_NoPermiteVolverAPendiente(
        EstadoBulto estadoFinal)
    {
        var bulto = new Bulto();

        bulto.CambiarEstado(estadoFinal);

        Assert.Throws<InvalidOperationException>(
            () => bulto.CambiarEstado(EstadoBulto.Pendiente));

        Assert.Equal(estadoFinal, bulto.Estado);
    }

    [Fact]
    public void Bulto_NoPermiteCambiarAlMismoEstado()
    {
        var bulto = new Bulto();

        Assert.Throws<InvalidOperationException>(
            () => bulto.CambiarEstado(EstadoBulto.Pendiente));
    }

    [Fact]
    public void BultoNoEntregado_PuedeSerDevuelto()
    {
        var bulto = new Bulto();

        bulto.CambiarEstado(EstadoBulto.NoEntregado);
        bulto.CambiarEstado(EstadoBulto.Devuelto);

        Assert.Equal(EstadoBulto.Devuelto, bulto.Estado);
    }
}