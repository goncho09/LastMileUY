namespace LastMileUY.Backoffice.Tests;

using System.Security.Claims;
using LastMileUY.Backoffice.Servicios;

public class IdentidadOperadorTests
{
    [Theory]
    [InlineData("#0B5CAD", "#0B5CAD")]
    [InlineData("#1e7f4f", "#1e7f4f")]
    [InlineData(null, IdentidadOperador.ColorPorDefecto)]
    [InlineData("red", IdentidadOperador.ColorPorDefecto)]
    [InlineData("#000; background: url(x)", IdentidadOperador.ColorPorDefecto)]
    public void Solo_se_usa_un_color_hexadecimal(string? color, string esperado)
    {
        Assert.Equal(esperado, IdentidadOperador.ColorSeguro(color));
    }

    [Theory]
    [InlineData("https://cdn.ejemplo.test/logo.png", true)]
    [InlineData("http://ejemplo.test/logo.png", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml;base64,AAAA", false)]
    [InlineData("/logo.png", false)]
    [InlineData(null, false)]
    public void Solo_se_usa_un_logo_por_http(string? url, bool seUsa)
    {
        Assert.Equal(seUsa, IdentidadOperador.LogoSeguro(url) is not null);
    }

    [Theory]
    [InlineData("#0B5CAD", "#ffffff")]
    [InlineData("#212529", "#ffffff")]
    [InlineData("#FFD700", "#212529")]
    [InlineData("#FFFFFF", "#212529")]
    public void El_texto_contrasta_con_el_color_del_operador(string color, string texto)
    {
        Assert.Equal(texto, IdentidadOperador.TextoSobre(color));
    }

    [Fact]
    public void Se_arma_desde_la_sesion()
    {
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(IdentidadOperador.ClaimNombre, "envíos rápidos"),
            new Claim(IdentidadOperador.ClaimColor, "#1E7F4F")
        ], "prueba"));

        var identidad = IdentidadOperador.Desde(usuario);

        Assert.NotNull(identidad);
        Assert.Equal("#1E7F4F", identidad.Color);
        Assert.Equal("#ffffff", identidad.ColorTexto);
        Assert.Null(identidad.LogoUrl);
        Assert.Equal("E", identidad.Inicial);
    }

    [Fact]
    public void Sin_operador_en_la_sesion_no_hay_identidad()
    {
        Assert.Null(IdentidadOperador.Desde(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
