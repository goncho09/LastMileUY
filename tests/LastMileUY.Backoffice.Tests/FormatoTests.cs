namespace LastMileUY.Backoffice.Tests;

using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Envios;

public class FormatoTests
{
    [Theory]
    [InlineData(false, false, false, "Nada")]
    [InlineData(true, false, false, "Firma")]
    [InlineData(false, true, true, "Foto y documento del receptor")]
    [InlineData(true, true, true, "Firma, foto y documento del receptor")]
    public void Los_requisitos_de_la_regla_se_leen_como_una_frase(
        bool firma, bool foto, bool documento, string esperado)
    {
        var regla = new ReglaAplicadaDto("Regla", 2, 24, 48, firma, foto, documento);

        Assert.Equal(esperado, Formato.Requisitos(regla));
    }
}
