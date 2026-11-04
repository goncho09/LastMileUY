namespace LastMileUY.DriverPWA.Tests;

using LastMileUY.Contracts.Rutas;
using LastMileUY.DriverPWA.Services;
using static LastMileUY.DriverPWA.Services.ResultadoPorBulto;

// Qué manda la app al confirmar una entrega con varios bultos: el repartidor marca cuáles entregó
// y, de los que no, el motivo. Con eso se decide si es una entrega o una entrega parcial.
public class ResultadoPorBultoTests
{
    private const int Danado = 3;

    private static readonly BultoRutaDto Uno = new(1, "B1", "Pendiente", null, 2);
    private static readonly BultoRutaDto Dos = new(2, "B2", "Pendiente", null, 2);
    // No se entregó en una ruta anterior: vuelve a salir y se puede entregar
    private static readonly BultoRutaDto Tres = new(3, "B3", "NoEntregado", null, 2);
    private static readonly BultoRutaDto YaEntregado = new(4, "B4", "Entregado", null, 2);

    private static readonly BultoRutaDto[] Bultos = [Uno, Dos, Tres, YaEntregado];

    [Fact]
    public void Si_escaneo_alguno_arrancan_entregados_solo_esos()
    {
        var marcas = MarcasIniciales(Bultos, [Dos.Id]);

        // El ya entregado no participa
        Assert.Equal([1, 2, 3], marcas.Keys.Order());
        Assert.False(marcas[Uno.Id].Entregado);
        Assert.True(marcas[Dos.Id].Entregado);
        Assert.False(marcas[Tres.Id].Entregado);
    }

    [Fact]
    public void Si_no_escaneo_ninguno_arrancan_todos_entregados()
    {
        var marcas = MarcasIniciales(Bultos, []);

        Assert.All(marcas.Values, m => Assert.True(m.Entregado));
    }

    [Fact]
    public void Todos_entregados_es_una_entrega_comun()
    {
        var decision = Decidir(Bultos, MarcasIniciales(Bultos, []), regla: null);

        Assert.Null(decision.Error);
        Assert.Equal("Entrega", decision.Tipo);
        Assert.Null(decision.Resultados);
    }

    [Fact]
    public void Sin_la_lista_de_bultos_es_una_entrega_comun()
    {
        var decision = Decidir([], new Dictionary<int, Marca>(), regla: null);

        Assert.Null(decision.Error);
        Assert.Equal("Entrega", decision.Tipo);
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
        var marcas = MarcasIniciales(Bultos, []);
        marcas.Remove(Tres.Id);

        var decision = Decidir(Bultos, marcas, regla: null);

        Assert.Equal("EntregaParcial", decision.Tipo);
        Assert.Contains("motivo", decision.Error);
    }
}
