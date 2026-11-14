using LastMileUY.Contracts.Deposito;

namespace LastMileUY.Backoffice.Servicios;

public sealed class DepositoApiDePrueba : IDepositoApi
{
    private readonly Lock candado = new();
    private readonly List<EnvioParaRecibirDto> envios =
    [
        Envio(501, "QK7P2M9XA4TB", "Tienda del Centro", "Lucía Pérez", (2.5m, "Ropa"), (1.2m, null)),
        Envio(502, "HB3N8R2WQ6YZ", "Librería Sur", "Martín Rodríguez", (4m, "Libros")),
        Envio(503, "TF6D9J4KC2LM", "Tienda del Centro", "Ana Silva", (0.8m, null), (3.1m, "Zapatos"), (1.5m, null))
    ];

    private readonly List<RecepcionDto> recepciones = [];
    private int ultimoId = 900;

    private static EnvioParaRecibirDto Envio(int id, string codigo, string comercio, string destinatario,
        params (decimal Peso, string? Descripcion)[] bultos) =>
        new(id, codigo, "Admitido", comercio, destinatario,
            bultos.Select((b, i) => new BultoPorRecibirDto(id * 10 + i, $"{codigo}-{i + 1}", b.Peso, 30, 20, 15, b.Descripcion)).ToList(),
            []);

    public Task<IReadOnlyList<EnvioPorRecibirDto>> PorRecibirAsync(CancellationToken ct = default)
    {
        lock (candado)
        {
            return Task.FromResult<IReadOnlyList<EnvioPorRecibirDto>>(envios
                .Where(e => e.SePuedeRecibir)
                .Select(e => new EnvioPorRecibirDto(e.Id, e.CodigoSeguimiento, e.Comercio, e.Destinatario,
                    DateTime.UtcNow.AddHours(-e.Id % 7), e.Bultos.Count, recepciones.Any(r => r.EnvioId == e.Id && !r.Aceptada)))
                .ToList());
        }
    }

    public Task<EnvioParaRecibirDto?> BuscarAsync(string codigo, CancellationToken ct = default)
    {
        var limpio = codigo.Trim().ToUpperInvariant();

        lock (candado)
        {
            var envio = envios.FirstOrDefault(e =>
                e.CodigoSeguimiento == limpio || e.Bultos.Any(b => b.Codigo == limpio));

            return Task.FromResult(envio is null
                ? null
                : envio with { Recepciones = recepciones.Where(r => r.EnvioId == envio.Id).ToList() });
        }
    }

    public Task<RecepcionDto> RecibirAsync(RegistrarRecepcionRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var envio = envios.FirstOrDefault(e => e.Id == request.EnvioId)
                ?? throw new OperacionRechazadaException("El envío ya no existe. Volvé a buscarlo.");

            if (!envio.SePuedeRecibir)
                throw new OperacionRechazadaException($"El envío {envio.CodigoSeguimiento} ya no está esperando ingreso.");

            if (request.Bultos.Count != envio.Bultos.Count)
                throw new OperacionRechazadaException("Falta indicar qué pasó con algún bulto.");

            var bultos = request.Bultos
                .Select(b =>
                {
                    var declarado = envio.Bultos.Single(x => x.Codigo == b.Codigo).PesoKg;
                    var distinto = b.PesoMedidoKg is { } medido && Math.Abs(medido - declarado) > Math.Max(0.1m, declarado * 0.1m);
                    return new BultoRecibidoDto(b.Codigo, b.Estado, declarado, b.PesoMedidoKg, distinto);
                })
                .ToList();

            var aceptada = bultos.All(b => b.Estado != EstadosBultoRecibido.Faltante);
            var recepcion = new RecepcionDto(++ultimoId, envio.Id, envio.CodigoSeguimiento, envio.Comercio, DateTime.UtcNow,
                aceptada, bultos.Any(b => b.Estado != EstadosBultoRecibido.Recibido || b.PesoDistinto),
                request.Observaciones, bultos, null, null);

            recepciones.Insert(0, recepcion);

            if (aceptada)
                envios[envios.IndexOf(envio)] = envio with { Estado = "EnDeposito" };

            return Task.FromResult(recepcion);
        }
    }

    public Task<IReadOnlyList<RecepcionDto>> DiscrepanciasAsync(bool soloPendientes, CancellationToken ct = default)
    {
        lock (candado)
        {
            return Task.FromResult<IReadOnlyList<RecepcionDto>>(recepciones
                .Where(r => r.TieneDiscrepancias && (!soloPendientes || r.Pendiente))
                .ToList());
        }
    }

    public Task ResolverAsync(int recepcionId, string resolucion, CancellationToken ct = default)
    {
        lock (candado)
        {
            var indice = recepciones.FindIndex(r => r.Id == recepcionId && r.Pendiente);

            if (indice < 0)
                throw new OperacionRechazadaException("Esta recepción no tiene discrepancias pendientes.");

            recepciones[indice] = recepciones[indice] with { FechaResolucion = DateTime.UtcNow, Resolucion = resolucion };
        }

        return Task.CompletedTask;
    }

    private readonly List<CargaRutaDto> cargas =
    [
        new(1, "Centro mañana", DateOnly.FromDateTime(DateTime.Today), "Planificada", "Carlos Méndez", "SBC 1234",
            EstadosCarga.SinVerificar, null,
        [
            new("KX4M7P2QW9TA-1", 601, "KX4M7P2QW9TA", 1, "Lucía Pérez", "Ropa", null),
            new("KX4M7P2QW9TA-2", 601, "KX4M7P2QW9TA", 1, "Lucía Pérez", null, null),
            new("PL8R3N6YD2HC-1", 602, "PL8R3N6YD2HC", 2, "Martín Rodríguez", "Libros", null),
            new("ZV5J9T4BM7QE-1", 603, "ZV5J9T4BM7QE", 3, "Ana Silva", null, null)
        ])
    ];

    public Task<IReadOnlyList<RutaPorCargarDto>> RutasPorCargarAsync(CancellationToken ct = default)
    {
        lock (candado)
        {
            return Task.FromResult<IReadOnlyList<RutaPorCargarDto>>(cargas
                .Where(c => c.SePuedeVerificar)
                .Select(c => new RutaPorCargarDto(c.RutaId, c.Nombre, c.Fecha, c.Repartidor, c.Vehiculo,
                    c.Bultos.Select(b => b.EnvioId).Distinct().Count(), c.Bultos.Count, c.EstadoCarga))
                .ToList());
        }
    }

    public Task<CargaRutaDto?> CargaAsync(int rutaId, CancellationToken ct = default)
    {
        lock (candado)
        {
            return Task.FromResult(cargas.FirstOrDefault(c => c.RutaId == rutaId));
        }
    }

    public Task<CargaRutaDto> VerificarCargaAsync(int rutaId, VerificarCargaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var indice = cargas.FindIndex(c => c.RutaId == rutaId);

            if (indice < 0)
                throw new OperacionRechazadaException("La ruta ya no existe.");

            var escaneados = request.BultosCargados.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
            var ajenos = escaneados.Where(c => cargas[indice].Bultos.All(b => b.Codigo != c)).ToList();

            if (ajenos.Count > 0)
                throw new OperacionRechazadaException($"No son de esta ruta: {string.Join(", ", ajenos)}.");

            var bultos = cargas[indice].Bultos.Select(b => b with { Cargado = escaneados.Contains(b.Codigo) }).ToList();

            cargas[indice] = cargas[indice] with
            {
                Bultos = bultos,
                UltimaVerificacion = DateTime.UtcNow,
                EstadoCarga = bultos.All(b => b.Cargado == true) ? EstadosCarga.Verificada : EstadosCarga.Incompleta
            };

            return Task.FromResult(cargas[indice]);
        }
    }

    public Task QuitarEnvioAsync(int rutaId, int envioId, CancellationToken ct = default)
    {
        lock (candado)
        {
            var indice = cargas.FindIndex(c => c.RutaId == rutaId);

            if (indice < 0)
                throw new OperacionRechazadaException("La ruta ya no existe.");

            var bultos = cargas[indice].Bultos.Where(b => b.EnvioId != envioId).ToList();

            cargas[indice] = cargas[indice] with
            {
                Bultos = bultos,
                EstadoCarga = cargas[indice].UltimaVerificacion is null ? EstadosCarga.SinVerificar
                    : bultos.All(b => b.Cargado == true) ? EstadosCarga.Verificada : EstadosCarga.Incompleta
            };
        }

        return Task.CompletedTask;
    }
}
