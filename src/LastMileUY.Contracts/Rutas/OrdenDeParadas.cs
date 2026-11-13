namespace LastMileUY.Contracts.Rutas;

public record Coordenadas(decimal Latitud, decimal Longitud)
{
    public static bool EsValida(decimal latitud, decimal longitud) =>
        latitud is >= -90 and <= 90 && longitud is >= -180 and <= 180;
}

public record ParadaAOrdenar(int Id, Coordenadas? Coordenadas, TimeOnly? InicioFranja);

public record OrdenCalculado(
    IReadOnlyList<int> Ids,
    double KmActual,
    double KmSugerido,
    IReadOnlyList<int> SinUbicacion);

public static class OrdenDeParadas
{
    private const double RadioTierraKm = 6371.0;

    private const int Arranques = 8;

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

    public static double DistanciaKm(Coordenadas a, Coordenadas b)
    {
        var lat1 = Radianes(a.Latitud);
        var lat2 = Radianes(b.Latitud);
        var dLat = lat2 - lat1;
        var dLon = Radianes(b.Longitud) - Radianes(a.Longitud);

        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);

        return 2 * RadioTierraKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

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

        public IEnumerable<int> Arranques()
        {
            var candidatas = Candidatas(Enumerable.Range(0, paradas.Count).ToList());

            return (desdeDeposito is null ? candidatas : candidatas.OrderBy(i => desdeDeposito[i]))
                .Take(OrdenDeParadas.Arranques);
        }

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

        private IEnumerable<int> Candidatas(List<int> restantes)
        {
            var primeraFranja = restantes.Select(i => paradas[i].InicioFranja).Where(f => f is not null).Min();

            return restantes.Where(i => paradas[i].InicioFranja is null || paradas[i].InicioFranja == primeraFranja);
        }

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
