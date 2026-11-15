using LastMileUY.API.Modules.Envios.Domain;

namespace LastMileUY.API.Tests.Envios;

public class DevolucionTests
{
    private static Envio EnvioNoEntregado()
    {
        var envio = new Envio();

        foreach (var estado in new[] { EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito, EstadoEnvio.NoEntregado })
            envio.CambiarEstado(estado);

        return envio;
    }

    [Fact]
    public void Pasar_a_devolucion_abre_la_devolucion_y_entregarla_al_comercio_la_cierra()
    {
        var envio = EnvioNoEntregado();
        var inicio = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        envio.CambiarEstado(EstadoEnvio.EnDevolucion, "Deposito", "Se agotaron los intentos.", ocurrido: inicio);

        Assert.NotNull(envio.Devolucion);
        Assert.Equal("Se agotaron los intentos.", envio.Devolucion.Motivo);
        Assert.Equal(inicio, envio.Devolucion.FechaInicio);
        Assert.Null(envio.Devolucion.FechaFinalizacion);

        envio.CambiarEstado(EstadoEnvio.Devuelto, ocurrido: inicio.AddDays(1));

        Assert.Equal(inicio.AddDays(1), envio.Devolucion.FechaFinalizacion);
    }

    [Fact]
    public void Sin_descripcion_la_devolucion_tiene_un_motivo_por_defecto()
    {
        var envio = EnvioNoEntregado();

        envio.CambiarEstado(EstadoEnvio.EnDevolucion);

        Assert.Equal("Vuelve al comercio.", envio.Devolucion!.Motivo);
    }
}
