using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Seguimiento.Application;

namespace LastMileUY.API.Tests.Envios;

public class PoliticaReprogramacionTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 15);

    private static readonly ReglaOperativa Regla = new() { Nombre = "Estándar", MaximoIntentosEntrega = 2, HorasEntreIntentos = 48 };

    [Theory]
    [InlineData(EstadoEnvio.NoEntregado, 1, false, null)]
    [InlineData(EstadoEnvio.EnDeposito, 0, false, null)]
    [InlineData(EstadoEnvio.EnTransito, 0, false, "en camino")]
    [InlineData(EstadoEnvio.Entregado, 0, false, "ya no se puede")]
    [InlineData(EstadoEnvio.NoEntregado, 2, false, "agotaron")]
    [InlineData(EstadoEnvio.NoEntregado, 1, true, "Ya pediste")]
    public void Decide_segun_el_estado_los_intentos_y_si_ya_lo_pidio(
        EstadoEnvio estado, int intentos, bool yaSolicito, string? motivoEsperado)
    {
        var (motivo, _, _) = PoliticaReprogramacion.Evaluar(estado, intentos, Regla, yaSolicito, Hoy);

        if (motivoEsperado is null)
            Assert.Null(motivo);
        else
            Assert.Contains(motivoEsperado, motivo);
    }

    [Fact]
    public void La_fecha_minima_respeta_las_horas_entre_intentos_de_la_regla()
    {
        var (_, minima, maxima) = PoliticaReprogramacion.Evaluar(EstadoEnvio.NoEntregado, 1, Regla, false, Hoy);

        Assert.Equal(Hoy.AddDays(2), minima);
        Assert.Equal(Hoy.AddDays(PoliticaReprogramacion.DiasMaximos), maxima);
    }
}
