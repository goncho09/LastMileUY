namespace LastMileUY.Contracts.Rutas;

// Un punto en el mapa, en grados decimales (los de cualquier GPS o mapa: WGS84)
public record Coordenadas(decimal Latitud, decimal Longitud)
{
    public static bool EsValida(decimal latitud, decimal longitud) =>
        latitud is >= -90 and <= 90 && longitud is >= -180 and <= 180;
}

// Una parada para ordenar: dónde queda (null si la dirección no tiene ubicación) y a qué hora
// empieza su franja horaria (null si no tiene)
public record ParadaAOrdenar(int Id, Coordenadas? Coordenadas, TimeOnly? InicioFranja);

// Kilómetros en línea recta del recorrido: desde el depósito (si el operador lo cargó) hasta la
// última parada con ubicación. KmActual es el del orden que se recibió; KmSugerido, el de Ids.
public record OrdenCalculado(
    IReadOnlyList<int> Ids,
    double KmActual,
    double KmSugerido,
    IReadOnlyList<int> SinUbicacion);

// Sugiere el orden de visita de una hoja de ruta para recorrer menos kilómetros
// (docs/hojas-de-ruta.md, "Orden sugerido"):
// 1. Vecino más cercano: desde el depósito, siempre a la parada más cercana que se pueda visitar.
// 2. Mejoras locales, mientras alguna acorte el recorrido:
//    - "2-opt": dar vuelta un tramo (saca los cruces);
//    - "or-opt": sacar de 1 a 3 paradas seguidas y ponerlas en otro lugar.
// 3. Se prueba arrancando por varias paradas (las más cercanas al depósito) y también mejorando el
//    orden recibido, y se queda el más corto. Así nunca queda peor que lo que armó el despachador.
// En todo momento respeta las franjas horarias (las paradas con franja van en orden de horario,
// la misma regla que controla la API al armar la ruta). Las paradas sin ubicación no se pueden
// ubicar en el recorrido: van al final, o antes de la primera franja posterior a la suya.
// Es una sugerencia: el despachador la puede cambiar antes de armar la ruta.
public static class OrdenDeParadas
{
    private const double RadioTierraKm = 6371.0;

    // Por cuántas paradas distintas se prueba arrancar
    private const int Arranques = 8;

    // Tope de mejoras por recorrido, para que nunca se quede dando vueltas (con 100 paradas sobra)
    private const int MaximoMejoras = 20_000;

    private const double Tolerancia = 1e-9;

    public static OrdenCalculado Sugerir(IReadOnlyList<ParadaAOrdenar> paradas, Coordenadas? deposito)
    {
        var conUbicacion = paradas.Where(p => p.Coordenadas is not null).ToList();
        var sinUbicacion = paradas.Where(p => p.Coordenadas is null).ToList();
        var problema = new Problema(conUbicacion, deposito);

        var recibido = Enumerable.Range(0, conUbicacion.Count).ToArray();
        var kmActual = problema.Km(recibido);

        int[]? mejor = null;
        var mejorKm = double.MaxValue;

        // Primero el recibido: ante un empate se queda ese, para no cambiar lo que no hace falta
        if (problema.RespetaFranjas(recibido))
        {
            mejor = problema.Mejorar(recibido);
            mejorKm = problema.Km(mejor);
        }

        foreach (var primera in problema.Arranques())
        {
            var recorrido = problema.Mejorar(problema.VecinoMasCercano(primera));
            var km = problema.Km(recorrido);

            if (km < mejorKm - Tolerancia)
            {
                mejor = recorrido;
                mejorKm = km;
            }
        }

        var final = mejor ?? recibido;
        var sugerido = final.Select(i => conUbicacion[i]).ToList();

        return new OrdenCalculado(
            ConSinUbicacion(sugerido, sinUbicacion).Select(p => p.Id).ToList(),
            Math.Round(kmActual, 1),
            Math.Round(problema.Km(final), 1),
            sinUbicacion.Select(p => p.Id).ToList());
    }

    // Las paradas con franja tienen que ir en orden de horario; las que no tienen, en cualquier lugar
    public static bool RespetaFranjas(IEnumerable<ParadaAOrdenar> enOrden)
    {
        TimeOnly? anterior = null;

        foreach (var inicio in enOrden.Select(p => p.InicioFranja).OfType<TimeOnly>())
        {
            if (anterior is { } previo && inicio < previo)
                return false;

            anterior = inicio;
        }

        return true;
    }

    // Distancia en línea recta entre dos puntos (fórmula del haversine)
    public static double DistanciaKm(Coordenadas a, Coordenadas b)
    {
        var lat1 = Radianes(a.Latitud);
        var lat2 = Radianes(b.Latitud);
        var dLat = lat2 - lat1;
        var dLon = Radianes(b.Longitud) - Radianes(a.Longitud);

        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);

        return 2 * RadioTierraKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    // Recorrido de las paradas en ese orden (todas con ubicación), desde el depósito si hay
    public static double Km(IReadOnlyList<ParadaAOrdenar> enOrden, Coordenadas? deposito)
    {
        var total = 0.0;
        var anterior = deposito;

        foreach (var parada in enOrden)
        {
            if (anterior is not null)
                total += DistanciaKm(anterior, parada.Coordenadas!);

            anterior = parada.Coordenadas;
        }

        return total;
    }

    // Las sin ubicación, en el orden recibido: al final, salvo que tengan franja y haya paradas de
    // una franja posterior; en ese caso, justo antes de la primera de esas
    private static List<ParadaAOrdenar> ConSinUbicacion(List<ParadaAOrdenar> recorrido, List<ParadaAOrdenar> sinUbicacion)
    {
        var resultado = new List<ParadaAOrdenar>(recorrido);

        foreach (var parada in sinUbicacion)
        {
            var posterior = parada.InicioFranja is { } inicio
                ? resultado.FindIndex(p => p.InicioFranja > inicio)
                : -1;

            if (posterior < 0)
                resultado.Add(parada);
            else
                resultado.Insert(posterior, parada);
        }

        return resultado;
    }

    private static double Radianes(decimal grados) => (double)grados * Math.PI / 180.0;

    // Las paradas con ubicación, por posición (0..n-1), con las distancias calculadas una sola vez.
    // Un recorrido es un arreglo de posiciones. Inicio (-1) es el depósito y Fin (-2) es después de
    // la última parada: el recorrido no vuelve al depósito.
    private sealed class Problema
    {
        private const int Inicio = -1;
        private const int Fin = -2;

        private readonly IReadOnlyList<ParadaAOrdenar> paradas;
        private readonly double[,] distancia;
        private readonly double[]? desdeDeposito;

        public Problema(IReadOnlyList<ParadaAOrdenar> paradas, Coordenadas? deposito)
        {
            this.paradas = paradas;
            var n = paradas.Count;
            distancia = new double[n, n];

            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    distancia[i, j] = distancia[j, i] = DistanciaKm(paradas[i].Coordenadas!, paradas[j].Coordenadas!);
                }
            }

            if (deposito is not null)
                desdeDeposito = paradas.Select(p => DistanciaKm(deposito, p.Coordenadas!)).ToArray();
        }

        // Lo que suma ir de a hasta b. Sin depósito, la primera parada no suma nada
        private double Tramo(int a, int b)
        {
            if (b == Fin)
                return 0;

            if (a == Inicio)
                return desdeDeposito?[b] ?? 0;

            return distancia[a, b];
        }

        public double Km(int[] recorrido)
        {
            var total = 0.0;
            var anterior = Inicio;

            foreach (var parada in recorrido)
            {
                total += Tramo(anterior, parada);
                anterior = parada;
            }

            return total;
        }

        public bool RespetaFranjas(int[] recorrido) =>
            OrdenDeParadas.RespetaFranjas(recorrido.Select(i => paradas[i]));

        // Por dónde se prueba arrancar: las que pueden ir primeras (por las franjas), las más
        // cercanas al depósito (sin depósito, en el orden recibido)
        public IEnumerable<int> Arranques()
        {
            var candidatas = Candidatas(Enumerable.Range(0, paradas.Count).ToList());

            return (desdeDeposito is null ? candidatas : candidatas.OrderBy(i => desdeDeposito[i]))
                .Take(OrdenDeParadas.Arranques);
        }

        // Desde la primera, siempre a la parada más cercana que se pueda visitar
        public int[] VecinoMasCercano(int primera)
        {
            var restantes = Enumerable.Range(0, paradas.Count).Where(i => i != primera).ToList();
            var recorrido = new List<int>(paradas.Count) { primera };

            while (restantes.Count > 0)
            {
                var actual = recorrido[^1];
                var siguiente = Candidatas(restantes).MinBy(i => distancia[actual, i]);

                recorrido.Add(siguiente);
                restantes.Remove(siguiente);
            }

            return [.. recorrido];
        }

        // Las que se pueden visitar ahora: las sin franja y las de la franja más temprana que queda
        private IEnumerable<int> Candidatas(List<int> restantes)
        {
            var primeraFranja = restantes.Select(i => paradas[i].InicioFranja).Where(f => f is not null).Min();

            return restantes.Where(i => paradas[i].InicioFranja is null || paradas[i].InicioFranja == primeraFranja);
        }

        // Aplica la primera mejora que encuentra (2-opt u or-opt) y vuelve a buscar, hasta que
        // ninguna acorte el recorrido. Cada cambio se evalúa por lo que suma y resta en sus
        // extremos, sin recalcular todo; las franjas se controlan solo si acorta.
        public int[] Mejorar(int[] recorrido)
        {
            for (var mejoras = 0; mejoras < MaximoMejoras; mejoras++)
            {
                var mejor = DandoVuelta(recorrido) ?? Moviendo(recorrido);

                if (mejor is null)
                    break;

                recorrido = mejor;
            }

            return recorrido;
        }

        // 2-opt: dar vuelta el tramo de la posición i a la j. Cambian solo las dos puntas:
        // (antes de i → i) y (j → después de j) pasan a (antes de i → j) y (i → después de j)
        private int[]? DandoVuelta(int[] r)
        {
            var n = r.Length;

            for (var i = 0; i < n - 1; i++)
            {
                var antes = i == 0 ? Inicio : r[i - 1];

                for (var j = i + 1; j < n; j++)
                {
                    var despues = j == n - 1 ? Fin : r[j + 1];
                    var diferencia = Tramo(antes, r[j]) + Tramo(r[i], despues) - Tramo(antes, r[i]) - Tramo(r[j], despues);

                    if (diferencia >= -Tolerancia)
                        continue;

                    var candidato = (int[])r.Clone();
                    Array.Reverse(candidato, i, j - i + 1);

                    if (RespetaFranjas(candidato))
                        return candidato;
                }
            }

            return null;
        }

        // Or-opt: sacar las paradas de la posición desde a desde + largo - 1 y ponerlas, en el mismo
        // orden, entre otras dos (x e y) del resto del recorrido
        private int[]? Moviendo(int[] r)
        {
            var n = r.Length;

            for (var largo = 1; largo <= 3 && largo < n; largo++)
            {
                for (var desde = 0; desde + largo <= n; desde++)
                {
                    var hasta = desde + largo - 1;
                    var antes = desde == 0 ? Inicio : r[desde - 1];
                    var despues = hasta == n - 1 ? Fin : r[hasta + 1];
                    var alSacar = Tramo(antes, despues) - Tramo(antes, r[desde]) - Tramo(r[hasta], despues);

                    var resto = r.Take(desde).Concat(r.Skip(hasta + 1)).ToArray();

                    for (var hueco = 0; hueco <= resto.Length; hueco++)
                    {
                        var x = hueco == 0 ? Inicio : resto[hueco - 1];
                        var y = hueco == resto.Length ? Fin : resto[hueco];

                        if (x == antes && y == despues)
                            continue;

                        var diferencia = alSacar + Tramo(x, r[desde]) + Tramo(r[hasta], y) - Tramo(x, y);

                        if (diferencia >= -Tolerancia)
                            continue;

                        var candidato = resto.Take(hueco)
                            .Concat(r.Skip(desde).Take(largo))
                            .Concat(resto.Skip(hueco))
                            .ToArray();

                        if (RespetaFranjas(candidato))
                            return candidato;
                    }
                }
            }

            return null;
        }
    }
}
