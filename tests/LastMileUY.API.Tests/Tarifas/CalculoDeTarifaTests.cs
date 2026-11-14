using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Tarifas.Application;
using LastMileUY.API.Modules.Tarifas.Domain;

namespace LastMileUY.API.Tests.Tarifas;

public class CalculoDeTarifaTests
{
    private static Tarifa Tarifa(params AjusteTarifa[] ajustes) => new()
    {
        PesoMinimoKg = 1m,
        PesoMaximoKg = 10m,
        PrecioBase = 200m,
        PrecioPorKgAdicional = 30m,
        Ajustes = ajustes.ToList()
    };

    [Theory]
    [InlineData(0.5, 200)]
    [InlineData(1, 200)]
    [InlineData(3.5, 275)]
    public void El_precio_es_la_base_mas_los_kilos_por_encima_del_minimo(decimal pesoKg, decimal esperado) =>
        Assert.Equal(esperado, TarificadorEnvios.Calcular(Tarifa(), pesoKg));

    [Fact]
    public void Los_ajustes_activos_suman_y_restan_porcentaje_y_los_inactivos_no_cuentan()
    {
        var tarifa = Tarifa(
            new AjusteTarifa { Tipo = TipoAjusteTarifa.Recargo, Porcentaje = 15m },
            new AjusteTarifa { Tipo = TipoAjusteTarifa.Bonificacion, Porcentaje = 5m },
            new AjusteTarifa { Tipo = TipoAjusteTarifa.Recargo, Porcentaje = 50m, Activo = false });

        Assert.Equal(302.50m, TarificadorEnvios.Calcular(tarifa, 3.5m));
    }

    [Fact]
    public void Una_bonificacion_mayor_al_cien_por_ciento_no_da_un_precio_negativo()
    {
        var tarifa = Tarifa(new AjusteTarifa { Tipo = TipoAjusteTarifa.Bonificacion, Porcentaje = 150m });

        Assert.Equal(0m, TarificadorEnvios.Calcular(tarifa, 2m));
    }

    [Fact]
    public void El_volumen_se_suma_en_metros_cubicos()
    {
        var bultos = new[]
        {
            new Bulto { LargoCm = 100, AnchoCm = 50, AltoCm = 20 },
            new Bulto { LargoCm = 10, AnchoCm = 10, AltoCm = 10 }
        };

        Assert.Equal(0.101m, TarificadorEnvios.VolumenM3(bultos));
    }

    [Fact]
    public void La_zona_con_localidades_gana_a_la_que_cubre_todo_el_departamento_y_no_importan_tildes_ni_mayusculas()
    {
        var todoMaldonado = new Zona { Id = 1, Nombre = "Maldonado", Departamento = "Maldonado" };
        var punta = new Zona { Id = 2, Nombre = "Punta", Departamento = "Maldonado", Localidades = ["Punta del Este", "La Barra"] };
        var zonas = new[] { todoMaldonado, punta };

        Assert.Same(punta, CoberturaZonas.Elegir(zonas, "maldonado", "  PUNTA  DEL ESTE "));
        Assert.Same(todoMaldonado, CoberturaZonas.Elegir(zonas, "Maldonado", "San Carlos"));
        Assert.Null(CoberturaZonas.Elegir(zonas, "Rocha", "La Barra"));
        Assert.Same(punta, CoberturaZonas.Elegir([punta], "Maldonado", "la barrá"));
    }
}
