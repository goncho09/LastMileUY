using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using LastMileUY.DriverPWA.Services;

namespace LastMileUY.DriverPWA.Tests;

using static ResultadoPorBulto;

public class ResultadoPorBultoTests
{
    private const int Danado = 3;

    private static readonly BultoRutaDto Uno = new(1, "B1", "Pendiente", null, 2);
    private static readonly BultoRutaDto Dos = new(2, "B2", "Pendiente", null, 2);
    private static readonly BultoRutaDto Tres = new(3, "B3", "NoEntregado", null, 2);
    private static readonly BultoRutaDto YaEntregado = new(4, "B4", "Entregado", null, 2);

    private static readonly BultoRutaDto[] Bultos = [Uno, Dos, Tres, YaEntregado];

    [Fact]
    public void Si_escaneo_alguno_arrancan_entregados_solo_esos()
    {
        var marcas = MarcasIniciales(Bultos, [Dos.Id]);

        Assert.Equal([1, 2, 3], marcas.Keys.Order());
        Assert.False(marcas[Uno.Id].Entregado);
        Assert.True(marcas[Dos.Id].Entregado);
        Assert.False(marcas[Tres.Id].Entregado);
    }

    [Fact]
    public void Si_no_escaneo_ninguno_arrancan_todos_sin_entregar()
    {
        var marcas = MarcasIniciales(Bultos, []);

        Assert.Equal(3, marcas.Count);
        Assert.All(marcas.Values, m => Assert.False(m.Entregado));
    }

    [Fact]
    public void Todos_entregados_es_una_entrega_con_la_lista_de_los_bultos_que_vio_el_repartidor()
    {
        var marcas = MarcasIniciales(
            Bultos,
            [Uno.Id, Dos.Id, Tres.Id]);

        var decision = Decidir(Bultos, marcas, regla: null);

        Assert.Null(decision.Error);
        Assert.Equal("Entrega", decision.Tipo);

        Assert.Equal(
            [
                new(1, true, null),
                new(2, true, null),
                new(3, true, null)
            ],
            decision.Resultados!);
    }

    [Fact]
    public void Sin_la_lista_de_bultos_es_una_entrega_comun()
    {
        var decision = Decidir([], new Dictionary<int, Marca>(), regla: null);

        Assert.Null(decision.Error);
        Assert.Equal("Entrega", decision.Tipo);
        Assert.Null(decision.Resultados);
    }

    [Fact]
    public void Algunos_entregados_es_una_entrega_parcial_con_el_resultado_y_el_motivo_de_cada_bulto()
    {
        var marcas = MarcasIniciales(Bultos, [Uno.Id]);
        marcas[Dos.Id].MotivoNoEntregaId = Danado;
        marcas[Tres.Id].MotivoNoEntregaId = Danado;

        var decision = Decidir(Bultos, marcas, new ReglaEntregaParcialRutaDto(1, true, "NuevaHojaDeRuta"));

        Assert.Null(decision.Error);
        Assert.Equal("EntregaParcial", decision.Tipo);
        Assert.Equal(
            [new(1, true, null), new(2, false, Danado), new(3, false, Danado)],
            decision.Resultados!);
    }

    [Fact]
    public void Un_bulto_no_entregado_sin_motivo_no_se_puede_guardar()
    {
        var marcas = MarcasIniciales(Bultos, [Uno.Id]);
        marcas[Dos.Id].MotivoNoEntregaId = Danado;

        var decision = Decidir(Bultos, marcas, regla: null);

        Assert.Contains("motivo", decision.Error);
        Assert.Null(decision.Resultados);
    }

    [Fact]
    public void Ninguno_entregado_no_es_una_entrega()
    {
        var marcas = MarcasIniciales(Bultos, []);
        foreach (var marca in marcas.Values)
        {
            marca.Entregado = false;
            marca.MotivoNoEntregaId = Danado;
        }

        var decision = Decidir(Bultos, marcas, regla: null);

        Assert.Contains("No se pudo entregar", decision.Error);
    }

    [Fact]
    public void Si_las_reglas_de_la_hoja_no_permiten_parciales_se_avisa_antes_de_guardar()
    {
        var marcas = MarcasIniciales(Bultos, [Uno.Id]);
        marcas[Dos.Id].MotivoNoEntregaId = Danado;
        marcas[Tres.Id].MotivoNoEntregaId = Danado;

        var decision = Decidir(Bultos, marcas, new ReglaEntregaParcialRutaDto(4, false, "Devolucion"));

        Assert.Contains("versión 4", decision.Error);
        Assert.Null(decision.Resultados);
    }

    [Fact]
    public void Un_bulto_sin_marca_nunca_se_da_por_entregado()
    {
        var marcas = MarcasIniciales(Bultos, [Uno.Id, Dos.Id]);

        marcas.Remove(Tres.Id);

        var decision = Decidir(Bultos, marcas, regla: null);

        Assert.Equal("EntregaParcial", decision.Tipo);
        Assert.Contains("motivo", decision.Error);
        Assert.Null(decision.Resultados);
    }

    [Fact]
    public void Sin_bultos_identificados_no_se_puede_confirmar_una_entrega()
    {
        var marcas = MarcasIniciales(Bultos, []);

        var decision = Decidir(Bultos, marcas, regla: null);

        Assert.NotNull(decision.Error);
        Assert.Contains("No marcaste ningún bulto", decision.Error);
        Assert.Null(decision.Resultados);
    }

    [Fact]
    public void El_resumen_de_una_pendiente_dice_que_paso_con_cada_bulto()
    {
        var operacion = new OperacionRequest
        {
            Tipo = "EntregaParcial",
            ResultadosBultos = [new(1, true), new(2, false, Danado), new(3, false, 99)]
        };

        var resumen = Resumen(operacion, Bultos, [new MotivoNoEntregaDto(Danado, "Bulto dañado", null)]);

        Assert.Equal(["B1: entregado", "B2: no entregado (Bulto dañado)", "B3: no entregado"], resumen);
    }

    [Fact]
    public void Una_pendiente_sin_resultados_por_bulto_no_tiene_resumen()
    {
        Assert.Empty(Resumen(new OperacionRequest { Tipo = "IntentoFallido" }, Bultos, []));
    }
}
