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
}
