
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using Xunit;

namespace LastMileUY.API.Tests.Envios;

public class ResultadosBultosTests
{
    [Fact]
    public void Entregar_todos_los_bultos_deja_el_envio_entregado()
    {
        var envio = CrearEnvioEnTransito(3);

        var resultados = envio.Bultos
            .Select(b => Resultado(b, true))
            .ToList();

        var intento = envio.RegistrarResultadoBultos(resultados);

        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
        Assert.True(intento.Exitoso);
        Assert.False(intento.Parcial);
        Assert.Equal(3, intento.ResultadosBultos.Count);

        Assert.All(envio.Bultos,
            b => Assert.Equal(EstadoBulto.Entregado, b.Estado));
    }

    [Fact]
    public void Entrega_parcial_deja_el_envio_entregado_parcialmente()
    {
        var envio = CrearEnvioEnTransito(3);
        var bultos = envio.Bultos.OrderBy(b => b.Id).ToList();

        var resultados = new List<ResultadoBultoIntento>
        {
            Resultado(bultos[0], true),
            Resultado(bultos[1], true),
            Resultado(bultos[2], false)
        };

        var intento = envio.RegistrarResultadoBultos(resultados);

        Assert.Equal(EstadoEnvio.EntregadoParcialmente, envio.Estado);
        Assert.False(intento.Exitoso);
        Assert.True(intento.Parcial);

        Assert.Equal(2, envio.Bultos.Count(
            b => b.Estado == EstadoBulto.Entregado));

        Assert.Single(envio.Bultos,
            b => b.Estado == EstadoBulto.NoEntregado);
    }

    [Fact]
    public void Segunda_visita_completa_la_entrega_parcial()
    {
        var envio = CrearEnvioEnTransito(3);
        var bultos = envio.Bultos.OrderBy(b => b.Id).ToList();

        // Primera visita: dos entregados, uno no entregado.
        var primerIntento = envio.RegistrarResultadoBultos(
        [
            Resultado(bultos[0], true),
            Resultado(bultos[1], true),
            Resultado(bultos[2], false)
        ]);

        Assert.Equal(EstadoEnvio.EntregadoParcialmente, envio.Estado);

        // Segunda visita: reprogramar y volver a despachar.
        envio.CambiarEstado(EstadoEnvio.Reprogramado);
        envio.CambiarEstado(EstadoEnvio.AsignadoARuta);
        envio.CambiarEstado(EstadoEnvio.EnTransito);

        var segundoIntento = envio.RegistrarResultadoBultos(
        [
            Resultado(bultos[2], true)
        ]);

        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
        Assert.Equal(2, envio.IntentosEntrega.Count);

        Assert.All(envio.Bultos,
            b => Assert.Equal(EstadoBulto.Entregado, b.Estado));

        Assert.Equal(3, primerIntento.ResultadosBultos.Count);
        Assert.Single(segundoIntento.ResultadosBultos);
        Assert.True(segundoIntento.Exitoso);
    }

    [Fact]
    public void Ningun_bulto_entregado_deja_el_envio_no_entregado()
    {
        var envio = CrearEnvioEnTransito(2);

        var intento = envio.RegistrarResultadoBultos(
            envio.Bultos.Select(b => Resultado(b, false)).ToList());

        Assert.Equal(EstadoEnvio.NoEntregado, envio.Estado);
        Assert.False(intento.Exitoso);
        Assert.False(intento.Parcial);

        Assert.All(envio.Bultos,
            b => Assert.Equal(EstadoBulto.NoEntregado, b.Estado));
    }

    [Fact]
    public void No_permite_resultados_duplicados()
    {
        var envio = CrearEnvioEnTransito(2);
        var bulto = envio.Bultos.First();

        Assert.Throws<InvalidOperationException>(() =>
            envio.RegistrarResultadoBultos(
            [
                Resultado(bulto, true),
                Resultado(bulto, true)
            ]));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.All(envio.Bultos,
            b => Assert.Equal(EstadoBulto.Pendiente, b.Estado));
    }

    [Fact]
    public void No_permite_omitir_bultos_pendientes()
    {
        var envio = CrearEnvioEnTransito(2);
        var bulto = envio.Bultos.First();

        Assert.Throws<InvalidOperationException>(() =>
            envio.RegistrarResultadoBultos(
            [
                Resultado(bulto, true)
            ]));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
    }

    [Fact]
    public void No_permite_entregar_un_bulto_ya_entregado()
    {
        var envio = CrearEnvioEnTransito(2);
        var bultos = envio.Bultos.OrderBy(b => b.Id).ToList();

        envio.RegistrarResultadoBultos(
        [
            Resultado(bultos[0], true),
            Resultado(bultos[1], false)
        ]);

        envio.CambiarEstado(EstadoEnvio.Reprogramado);
        envio.CambiarEstado(EstadoEnvio.AsignadoARuta);
        envio.CambiarEstado(EstadoEnvio.EnTransito);

        Assert.Throws<InvalidOperationException>(() =>
            envio.RegistrarResultadoBultos(
            [
                Resultado(bultos[0], true)
            ]));

        Assert.Equal(EstadoBulto.Entregado, bultos[0].Estado);
        Assert.Equal(EstadoBulto.NoEntregado, bultos[1].Estado);
    }

    private static Envio CrearEnvioEnTransito(int cantidad)
    {
        var envio = new Envio();

        for (var i = 1; i <= cantidad; i++)
        {
            envio.Bultos.Add(new Bulto
            {
                Id = i,
                Codigo = $"BULTO-{i}",
                PesoKg = 1,
                LargoCm = 10,
                AnchoCm = 10,
                AltoCm = 10
            });
        }

        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        envio.CambiarEstado(EstadoEnvio.AsignadoARuta);
        envio.CambiarEstado(EstadoEnvio.EnTransito);

        return envio;
    }

    private static ResultadoBultoIntento Resultado(
        Bulto bulto,
        bool entregado)
    {
        return new ResultadoBultoIntento
        {
            BultoId = bulto.Id,
            Entregado = entregado,
            MotivoNoEntregaId = entregado ? null : 1
        };
    }
}
