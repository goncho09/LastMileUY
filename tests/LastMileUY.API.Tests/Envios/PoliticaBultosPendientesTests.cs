using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Xunit;

namespace LastMileUY.API.Tests.Envios;

// Qué pasa con los bultos que quedaron sin entregar después de una entrega parcial,
// según la política del operador (Envio.AplicarPoliticaBultosPendientes)
public class PoliticaBultosPendientesTests
{
    private static readonly ReglaOperativa DosIntentos = new() { Nombre = "Estándar", MaximoIntentosEntrega = 2 };
    private static readonly ReglaOperativa UnIntento = new() { Nombre = "Urgente", MaximoIntentosEntrega = 1 };

    [Fact]
    public void Con_nueva_hoja_de_ruta_el_envio_se_reprograma_y_lo_pendiente_queda_para_salir()
    {
        var envio = EnvioEntregadoParcialmente();

        envio.AplicarPoliticaBultosPendientes(Version(DestinoBultosPendientes.NuevaHojaDeRuta), DosIntentos);

        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        Assert.Single(envio.Bultos, b => b.Estado == EstadoBulto.NoEntregado);
        Assert.Equal("Sistema", envio.Eventos.Last().Origen);
        Assert.Contains("v3", envio.Eventos.Last().Descripcion);
    }

    // Manda el límite de intentos de la regla operativa: no se reprograma sin fin
    [Fact]
    public void Con_nueva_hoja_de_ruta_pero_sin_intentos_disponibles_va_a_devolucion()
    {
        var envio = EnvioEntregadoParcialmente();

        envio.AplicarPoliticaBultosPendientes(Version(DestinoBultosPendientes.NuevaHojaDeRuta), UnIntento);

        Assert.Equal(EstadoEnvio.EnDevolucion, envio.Estado);
        Assert.Contains("se agotaron los intentos", envio.Eventos.Last().Descripcion);
    }

    [Fact]
    public void Con_devolucion_el_envio_pasa_a_devolucion()
    {
        var envio = EnvioEntregadoParcialmente();

        envio.AplicarPoliticaBultosPendientes(Version(DestinoBultosPendientes.Devolucion), DosIntentos);

        Assert.Equal(EstadoEnvio.EnDevolucion, envio.Estado);
    }

    [Fact]
    public void Si_el_envio_no_quedo_entregado_parcialmente_no_cambia()
    {
        var envio = EnvioEntregadoParcialmente();
        envio.CambiarEstado(EstadoEnvio.Reprogramado);

        envio.AplicarPoliticaBultosPendientes(Version(DestinoBultosPendientes.Devolucion), DosIntentos);

        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
    }

    // Al terminar la devolución vuelven al comercio solo los que no se entregaron
    [Fact]
    public void Al_quedar_devuelto_los_bultos_no_entregados_quedan_devueltos_y_los_entregados_no_cambian()
    {
        var envio = EnvioEntregadoParcialmente();
        envio.AplicarPoliticaBultosPendientes(Version(DestinoBultosPendientes.Devolucion), DosIntentos);

        envio.CambiarEstado(EstadoEnvio.Devuelto);

        Assert.Equal(EstadoBulto.Entregado, envio.Bultos.Single(b => b.Id == 1).Estado);
        Assert.Equal(EstadoBulto.Devuelto, envio.Bultos.Single(b => b.Id == 2).Estado);
    }

    private static ReglaEntregaParcial Version(DestinoBultosPendientes destino) => new()
    {
        Version = 3,
        PermiteEntregaParcial = true,
        DestinoBultosPendientes = destino
    };

    // Dos bultos en tránsito: el 1 se entrega y el 2 no
    private static Envio EnvioEntregadoParcialmente()
    {
        var envio = new Envio();

        for (var i = 1; i <= 2; i++)
        {
            envio.Bultos.Add(new Bulto { Id = i, Codigo = $"BULTO-{i}", PesoKg = 1, LargoCm = 10, AnchoCm = 10, AltoCm = 10 });
        }

        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        envio.CambiarEstado(EstadoEnvio.AsignadoARuta);
        envio.CambiarEstado(EstadoEnvio.EnTransito);

        envio.RegistrarResultadoBultos(
        [
            new ResultadoBultoIntento { BultoId = 1, Entregado = true },
            new ResultadoBultoIntento { BultoId = 2, Entregado = false, MotivoNoEntregaId = 1 }
        ]);

        return envio;
    }
}
