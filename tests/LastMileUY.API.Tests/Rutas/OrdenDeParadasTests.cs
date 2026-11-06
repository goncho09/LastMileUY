namespace LastMileUY.API.Tests.Rutas;

using LastMileUY.Contracts.Rutas;
using static LastMileUY.Contracts.Rutas.OrdenDeParadas;

// El orden sugerido de las paradas (OrdenDeParadas): que recorra menos, que respete las franjas
// horarias como lo exige la API al armar la ruta, y qué pasa con las direcciones sin ubicación.
// Los puntos van sobre una misma latitud cerca de Montevideo: un paso de 0,01° de longitud son
// unos 0,9 km, así es fácil ver cuál queda más cerca.
public class OrdenDeParadasTests
{
    private static readonly TimeOnly Manana = new(9, 0);
    private static readonly TimeOnly Tarde = new(14, 0);

    private static readonly Coordenadas Deposito = En(0);

    private static Coordenadas En(int paso) => new(-34.9m, -56.2m + paso * 0.01m);

    private static ParadaAOrdenar Parada(int id, int? paso, TimeOnly? franja = null) =>
        new(id, paso is { } p ? En(p) : null, franja);

    [Fact]
    public void Desde_el_deposito_va_siempre_a_la_parada_mas_cercana()
    {
        var orden = Sugerir([Parada(3, 3), Parada(1, 1), Parada(2, 2)], Deposito);

        Assert.Equal([1, 2, 3], orden.Ids);
        Assert.True(orden.KmSugerido < orden.KmActual);
        Assert.Empty(orden.SinUbicacion);
    }

    [Fact]
    public void Si_el_orden_recibido_ya_era_el_mejor_lo_deja_igual()
    {
        var orden = Sugerir([Parada(1, 1), Parada(2, 2), Parada(3, 3)], Deposito);

        Assert.Equal([1, 2, 3], orden.Ids);
        Assert.Equal(orden.KmActual, orden.KmSugerido);
    }

    [Fact]
    public void La_franja_de_la_manana_va_antes_aunque_la_de_la_tarde_quede_mas_cerca()
    {
        var orden = Sugerir([Parada(1, 1, Tarde), Parada(2, 5, Manana)], Deposito);

        Assert.Equal([2, 1], orden.Ids);
    }

    [Fact]
    public void Una_parada_sin_franja_puede_ir_entre_las_de_la_manana_y_las_de_la_tarde()
    {
        // La sin franja queda en el camino: va en el medio, no al final
        var orden = Sugerir([Parada(1, 6, Tarde), Parada(2, 1, Manana), Parada(3, 3)], Deposito);

        Assert.Equal([2, 3, 1], orden.Ids);
    }

    [Fact]
    public void Sin_ubicacion_va_al_final_y_se_informa()
    {
        var orden = Sugerir([Parada(9, null), Parada(2, 2), Parada(1, 1)], Deposito);

        Assert.Equal([1, 2, 9], orden.Ids);
        Assert.Equal([9], orden.SinUbicacion);
    }

    [Fact]
    public void Sin_ubicacion_pero_con_franja_de_la_manana_va_antes_de_las_de_la_tarde()
    {
        var orden = Sugerir([Parada(1, 1, Tarde), Parada(9, null, Manana), Parada(2, 2, Tarde)], Deposito);

        Assert.Equal([9, 1, 2], orden.Ids);
    }

    [Fact]
    public void Sin_deposito_arranca_por_la_punta_que_deja_el_recorrido_mas_corto()
    {
        // Empezar por el 2 (en el medio) obliga a volver; por una punta se recorre una sola vez
        var orden = Sugerir([Parada(2, 2), Parada(3, 3), Parada(1, 1)], deposito: null);

        Assert.True(orden.Ids.SequenceEqual([1, 2, 3]) || orden.Ids.SequenceEqual([3, 2, 1]));
        Assert.Equal(DistanciaKm(En(1), En(3)), orden.KmSugerido, precision: 1);
    }

    [Fact]
    public void Un_recorrido_en_zigzag_se_desenreda()
    {
        // Las cuatro esquinas de un cuadrado, recibidas cruzadas (a, c, b, d): se recorren por el borde
        var a = new ParadaAOrdenar(1, new Coordenadas(-34.90m, -56.20m), null);
        var b = new ParadaAOrdenar(2, new Coordenadas(-34.90m, -56.18m), null);
        var c = new ParadaAOrdenar(3, new Coordenadas(-34.92m, -56.18m), null);
        var d = new ParadaAOrdenar(4, new Coordenadas(-34.92m, -56.20m), null);

        var orden = Sugerir([a, c, b, d], new Coordenadas(-34.90m, -56.21m));

        Assert.Equal([1, 2, 3, 4], orden.Ids);
        Assert.True(orden.KmSugerido < orden.KmActual);
    }

    // Contra el mejor recorrido posible, calculado probando todos los órdenes (con pocas paradas se
    // puede): el sugerido queda cerca. El vecino más cercano solo, sin la mejora 2-opt, no alcanza.
    [Fact]
    public void Queda_cerca_del_mejor_recorrido_posible()
    {
        var azar = new Random(611);
        var peorDiferencia = 0.0;

        for (var caso = 0; caso < 150; caso++)
        {
            var paradas = Enumerable.Range(1, 7)
                .Select(id => new ParadaAOrdenar(
                    id, new Coordenadas(-34.95m + azar.Next(100) * 0.001m, -56.25m + azar.Next(150) * 0.001m), null))
                .ToList();
            var deposito = new Coordenadas(-34.90m, -56.20m);

            var mejor = Permutaciones(paradas).Min(p => Km(p, deposito));
            var sugerido = Sugerir(paradas, deposito).KmSugerido;

            peorDiferencia = Math.Max(peorDiferencia, (sugerido - mejor) / mejor);
        }

        Assert.True(peorDiferencia < 0.05, $"En algún caso recorre un {peorDiferencia:P0} más que el mejor");
    }

    // El máximo de una ruta (100 paradas) se calcula mientras el despachador espera la página
    [Fact]
    public void Con_el_maximo_de_paradas_contesta_rapido()
    {
        var azar = new Random(100);
        TimeOnly?[] franjas = [null, null, Manana, Tarde];
        var paradas = Enumerable.Range(1, 100)
            .Select(id => new ParadaAOrdenar(
                id, new Coordenadas(-34.95m + azar.Next(100) * 0.001m, -56.25m + azar.Next(150) * 0.001m),
                franjas[azar.Next(franjas.Length)]))
            .ToList();

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var orden = Sugerir(paradas, Deposito);
        reloj.Stop();

        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(3), $"Tardó {reloj.Elapsed.TotalSeconds:0.0} s");
        Assert.True(orden.KmSugerido < orden.KmActual);
    }

    private static IEnumerable<List<ParadaAOrdenar>> Permutaciones(List<ParadaAOrdenar> paradas)
    {
        if (paradas.Count <= 1)
        {
            yield return paradas;
            yield break;
        }

        foreach (var primera in paradas)
        {
            foreach (var resto in Permutaciones(paradas.Where(p => p != primera).ToList()))
                yield return [primera, .. resto];
        }
    }

    // Con muchas combinaciones al azar (siempre las mismas): no se pierde ni repite ninguna parada,
    // siempre respeta las franjas y, si lo recibido las respetaba, nunca recorre más
    [Fact]
    public void Con_cualquier_combinacion_respeta_las_franjas_y_nunca_empeora()
    {
        var azar = new Random(20261106);
        TimeOnly?[] franjas = [null, Manana, new TimeOnly(11, 0), Tarde];

        for (var caso = 0; caso < 300; caso++)
        {
            var paradas = Enumerable.Range(1, azar.Next(1, 13))
                .Select(id => new ParadaAOrdenar(
                    id,
                    azar.Next(8) == 0 ? null : new Coordenadas(-34.95m + azar.Next(100) * 0.001m, -56.25m + azar.Next(150) * 0.001m),
                    franjas[azar.Next(franjas.Length)]))
                .ToList();
            var deposito = azar.Next(2) == 0 ? null : new Coordenadas(-34.86m, -56.21m);

            var orden = Sugerir(paradas, deposito);

            Assert.Equal(paradas.Select(p => p.Id).Order(), orden.Ids.Order());
            var enOrden = orden.Ids.Select(id => paradas.Single(p => p.Id == id)).ToList();
            Assert.True(RespetaFranjas(enOrden), $"Caso {caso}: no respeta las franjas");

            var conUbicacion = paradas.Where(p => p.Coordenadas is not null).ToList();
            if (RespetaFranjas(conUbicacion))
                Assert.True(orden.KmSugerido <= orden.KmActual, $"Caso {caso}: recorre más que lo recibido");
        }
    }

    [Fact]
    public void La_distancia_de_Montevideo_a_Buenos_Aires_es_de_unos_200_km()
    {
        var montevideo = new Coordenadas(-34.9011m, -56.1645m);
        var buenosAires = new Coordenadas(-34.6037m, -58.3816m);

        Assert.InRange(DistanciaKm(montevideo, buenosAires), 200, 210);
    }

    [Fact]
    public void Sin_paradas_con_ubicacion_no_hay_recorrido()
    {
        var orden = Sugerir([Parada(1, null), Parada(2, null)], Deposito);

        Assert.Equal([1, 2], orden.Ids);
        Assert.Equal(0, orden.KmSugerido);
        Assert.Equal([1, 2], orden.SinUbicacion);
    }
}
