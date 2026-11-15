using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Reportes.Application;

namespace LastMileUY.API.Tests.Envios;

public class ReportesTests
{
    private static readonly DateTime Alta = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ReportesService.FilaEnvio Envio(int id, int comercio, string? zona, EstadoEnvio estado,
        DateTime? entrega = null, decimal? costo = 100, int? plazo = 24) =>
        new(id, comercio, $"Comercio {comercio}", zona, estado, Alta, entrega, costo, plazo);

    [Fact]
    public void A_tiempo_es_entregado_dentro_del_plazo_de_su_regla()
    {
        Assert.True(ReportesService.ATiempo(Envio(1, 1, null, EstadoEnvio.Entregado, Alta.AddHours(20))));
        Assert.False(ReportesService.ATiempo(Envio(2, 1, null, EstadoEnvio.Entregado, Alta.AddHours(30))));
        Assert.True(ReportesService.ATiempo(Envio(3, 1, null, EstadoEnvio.Entregado, Alta.AddHours(30), plazo: null)));
        Assert.False(ReportesService.ATiempo(Envio(4, 1, null, EstadoEnvio.NoEntregado)));
    }

    [Fact]
    public void Agrupa_el_cumplimiento_por_zona()
    {
        var envios = new[]
        {
            Envio(1, 1, "Centro", EstadoEnvio.Entregado, Alta.AddHours(5)),
            Envio(2, 1, "Centro", EstadoEnvio.Entregado, Alta.AddHours(40)),
            Envio(3, 2, "Centro", EstadoEnvio.Devuelto),
            Envio(4, 2, null, EstadoEnvio.EnTransito)
        };

        var porZona = ReportesService.Cumplimiento(envios, e => e.Zona ?? "Sin zona");
        var centro = Assert.Single(porZona, z => z.Nombre == "Centro");

        Assert.Equal((3, 2, 1, 1), (centro.Envios, centro.Entregados, centro.ATiempo, centro.NoEntregados));
        Assert.Equal(33.3m, centro.PorcentajeATiempo);
        Assert.Equal(1, Assert.Single(porZona, z => z.Nombre == "Sin zona").Envios);
    }

    [Fact]
    public void La_liquidacion_suma_el_costo_de_cada_comercio_y_cuenta_los_que_no_tienen_tarifa()
    {
        var liquidacion = ReportesService.Liquidacion(
        [
            Envio(1, 1, null, EstadoEnvio.Entregado, Alta, costo: 150.5m),
            Envio(2, 1, null, EstadoEnvio.Devuelto, costo: 80),
            Envio(3, 1, null, EstadoEnvio.Admitido, costo: null),
            Envio(4, 2, null, EstadoEnvio.Entregado, Alta, costo: 90)
        ]);

        var primero = liquidacion.Single(l => l.ComercioId == 1);
        Assert.Equal((3, 1, 1, 1, 230.5m), (primero.Envios, primero.Entregados, primero.Devueltos, primero.SinTarifa, primero.Importe));
        Assert.Equal(90m, liquidacion.Single(l => l.ComercioId == 2).Importe);
    }
}
