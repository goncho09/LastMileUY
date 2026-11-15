using LastMileUY.Worker;

namespace LastMileUY.Worker.Tests;

public class PlantillaCorreoSeguimientoTests
{
    private static DatosCorreoSeguimiento Datos(string? operador = null, string? color = null, string? logo = null) =>
        new("ana@correo.test", "Ana", "Silva", "Tienda Sur", "K7MP3QXA9RTE", "https://seguimiento.test/seguimiento/K7MP3QXA9RTE",
            operador, color, logo);

    [Fact]
    public void Lleva_el_nombre_el_color_y_el_logo_del_operador()
    {
        var correo = PlantillaCorreoSeguimiento.Armar(
            Datos("Envíos Rápidos", "#0EA5E9", "https://cdn.test/logo.png"), PlantillaCorreoSeguimiento.Para("EnTransito")!);

        Assert.EndsWith("- Envíos Rápidos", correo.Asunto);
        Assert.Contains("Envíos Rápidos - Seguimiento de envíos", correo.Texto);
        Assert.Contains("#0EA5E9", correo.Html);
        Assert.DoesNotContain("#4d3fa0", correo.Html);
        Assert.Contains("https://cdn.test/logo.png", correo.Html);
    }

    [Fact]
    public void Sin_marca_valida_usa_la_de_la_plataforma_y_no_inyecta_nada()
    {
        var correo = PlantillaCorreoSeguimiento.Armar(
            Datos("<b>Malo</b>", "red;background:url(x)", "http://inseguro.test/logo.png"), PlantillaCorreoSeguimiento.Para("Entregado")!);

        Assert.Contains("#4d3fa0", correo.Html);
        Assert.DoesNotContain("red;background", correo.Html);
        Assert.DoesNotContain("inseguro.test", correo.Html);
        Assert.DoesNotContain("<b>Malo</b>", correo.Html);
        Assert.Contains("&lt;b&gt;Malo&lt;/b&gt;", correo.Html);

        Assert.EndsWith("- LastMileUY", PlantillaCorreoSeguimiento.Armar(Datos(), PlantillaCorreoSeguimiento.Para("Entregado")!).Asunto);
    }
}
