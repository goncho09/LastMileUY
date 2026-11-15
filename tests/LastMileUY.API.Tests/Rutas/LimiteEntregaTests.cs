using LastMileUY.API.Modules.Flota.Application;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Tests.Rutas;

public class LimiteEntregaTests
{
    private static readonly DateOnly Dia = new(2026, 11, 16);
    private static readonly FranjaHoraria Tarde = new() { Nombre = "Tarde", HoraInicio = new(14, 0), HoraFin = new(18, 0) };

    [Fact]
    public void Sin_franja_ni_plazo_no_hay_limite() =>
        Assert.Null(FlotaService.Limite(Dia, null, DateTime.UtcNow, null));

    [Fact]
    public void La_franja_cierra_en_hora_de_Uruguay()
    {
        var (limite, motivo) = FlotaService.Limite(Dia, Tarde, new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), null)!.Value;

        Assert.Equal(new DateTime(2026, 11, 16, 21, 0, 0, DateTimeKind.Utc), limite);
        Assert.Contains("18:00", motivo);
    }

    [Fact]
    public void Gana_el_limite_mas_cercano()
    {
        var creado = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var (limite, motivo) = FlotaService.Limite(Dia, Tarde, creado, 4)!.Value;

        Assert.Equal(creado.AddHours(4), limite);
        Assert.Contains("Plazo", motivo);
    }
}
