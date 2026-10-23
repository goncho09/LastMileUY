namespace LastMileUY.Worker.Tests;

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using static EntornoDelWorker;

// El consumidor contra un RabbitMQ real, con un procesador de prueba que falla las veces que se le
// pida: confirma lo procesado, reintenta con espera creciente y, agotados los reintentos, deja el
// mensaje en la cola de fallidos. Las esperas son cortas para que la prueba no tarde.
[Collection(ColeccionEntornoDelWorker.Nombre)]
public class ConsumidorEventosEnvioTests(EntornoDelWorker entorno) : IAsyncLifetime
{
    private static readonly TimeSpan[] Esperas =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800)];

    private readonly ProcesadorDePrueba procesador = new();
    private ConsumidorEventosEnvio consumidor = null!;
    private IConnection conexion = null!;
    private IChannel canal = null!;

    public async Task InitializeAsync()
    {
        consumidor = new ConsumidorEventosEnvio(
            new OpcionesConsumidor(entorno.CadenaRabbit, Esperas), procesador,
            NullLogger<ConsumidorEventosEnvio>.Instance);
        await consumidor.StartAsync(CancellationToken.None);

        conexion = await new ConnectionFactory { Uri = new Uri(entorno.CadenaRabbit) }.CreateConnectionAsync();
        canal = await conexion.CreateChannelAsync();

        // Que el consumidor ya haya declarado sus colas antes de publicar
        await EsperarAsync(async () => await ExisteAsync(ConsumidorEventosEnvio.ColaFallidos));
    }

    [Fact]
    public async Task Procesa_el_evento_y_lo_confirma()
    {
        var evento = Evento(OperadorA, EnvioDeA);

        await PublicarAsync(evento);

        await EsperarAsync(() => Task.FromResult(procesador.Exitosos.Contains(evento.EventoId)));
        Assert.Equal(1, procesador.Llamadas(evento.EventoId));
    }

    [Fact]
    public async Task Si_falla_lo_reintenta_y_termina_procesado()
    {
        var evento = Evento(OperadorA, EnvioDeA);
        procesador.FallarVeces(evento.EventoId, 2);

        await PublicarAsync(evento);

        await EsperarAsync(() => Task.FromResult(procesador.Exitosos.Contains(evento.EventoId)));
        Assert.Equal(3, procesador.Llamadas(evento.EventoId));
        Assert.Null(await BuscarEnFallidosAsync(evento.EventoId));
    }

    [Fact]
    public async Task Agotados_los_reintentos_queda_en_fallidos_con_el_error()
    {
        var evento = Evento(OperadorA, EnvioDeA);
        procesador.FallarVeces(evento.EventoId, int.MaxValue);

        await PublicarAsync(evento);

        var fallido = await EsperarFallidoAsync(evento.EventoId);
        Assert.Equal(1 + Esperas.Length, procesador.Llamadas(evento.EventoId));
        Assert.Equal(1 + Esperas.Length, Convert.ToInt32(fallido.Headers![ConsumidorEventosEnvio.EncabezadoIntentos]));
        Assert.Equal("Falla de prueba",
            Encoding.UTF8.GetString((byte[])fallido.Headers[ConsumidorEventosEnvio.EncabezadoError]!));
        // Conserva la correlación del pedido original
        Assert.Equal(evento.CorrelacionId, fallido.CorrelationId);
    }

    [Fact]
    public async Task Un_mensaje_ilegible_va_directo_a_fallidos_sin_procesarlo()
    {
        var id = Guid.NewGuid();

        await canal.BasicPublishAsync(TopologiaMensajeria.ExchangeEnvios, TiposEvento.EnvioEstadoCambiado,
            mandatory: true, basicProperties: new BasicProperties { MessageId = id.ToString() },
            body: Encoding.UTF8.GetBytes("esto no es un evento"));

        await EsperarFallidoAsync(id);
        Assert.Equal(0, procesador.Llamadas(id));
    }

    private async Task PublicarAsync(EstadoEnvioCambiado evento) =>
        await canal.BasicPublishAsync(TopologiaMensajeria.ExchangeEnvios, evento.Tipo, mandatory: true,
            basicProperties: new BasicProperties
            {
                MessageId = evento.EventoId.ToString(),
                CorrelationId = evento.CorrelacionId,
                ContentType = "application/json"
            },
            body: JsonSerializer.SerializeToUtf8Bytes(evento));

    private async Task<IReadOnlyBasicProperties> EsperarFallidoAsync(Guid id)
    {
        IReadOnlyBasicProperties? encontrado = null;
        await EsperarAsync(async () => (encontrado = await BuscarEnFallidosAsync(id)) is not null);
        return encontrado!;
    }

    // Saca de la cola de fallidos el mensaje con ese id (si está); los demás vuelven a la cola
    private async Task<IReadOnlyBasicProperties?> BuscarEnFallidosAsync(Guid id)
    {
        var vistos = new List<ulong>();
        IReadOnlyBasicProperties? encontrado = null;

        while (await canal.BasicGetAsync(ConsumidorEventosEnvio.ColaFallidos, autoAck: false) is { } mensaje)
        {
            if (mensaje.BasicProperties.MessageId == id.ToString())
            {
                encontrado = mensaje.BasicProperties;
                await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false);
                break;
            }

            vistos.Add(mensaje.DeliveryTag);
        }

        foreach (var tag in vistos)
            await canal.BasicNackAsync(tag, multiple: false, requeue: true);

        return encontrado;
    }

    private async Task<bool> ExisteAsync(string cola)
    {
        // passive: falla si la cola no existe (y cierra el canal), así que usa uno propio
        await using var prueba = await conexion.CreateChannelAsync();

        try
        {
            await prueba.QueueDeclarePassiveAsync(cola);
            return true;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return false;
        }
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException("No se cumplió la condición a tiempo.");
    }

    public async Task DisposeAsync()
    {
        await consumidor.StopAsync(CancellationToken.None);
        await canal.DisposeAsync();
        await conexion.DisposeAsync();
    }

    // Anota cada llamada y falla las veces que se le pidió para cada evento
    private class ProcesadorDePrueba : IProcesadorEventos
    {
        private readonly ConcurrentDictionary<Guid, int> fallasPendientes = new();
        private readonly ConcurrentDictionary<Guid, int> llamadas = new();

        public ConcurrentBag<Guid> Exitosos { get; } = [];

        public void FallarVeces(Guid eventoId, int veces) => fallasPendientes[eventoId] = veces;

        public int Llamadas(Guid eventoId) => llamadas.GetValueOrDefault(eventoId);

        public Task<ResultadoProceso> ProcesarAsync(EstadoEnvioCambiado evento, CancellationToken cancellationToken)
        {
            llamadas.AddOrUpdate(evento.EventoId, 1, (_, n) => n + 1);

            if (fallasPendientes.TryGetValue(evento.EventoId, out var veces) && veces > 0)
            {
                fallasPendientes[evento.EventoId] = veces - 1;
                throw new InvalidOperationException("Falla de prueba");
            }

            Exitosos.Add(evento.EventoId);
            return Task.FromResult(new ResultadoProceso(Repetido: false, AvisosRegistrados: 0));
        }
    }
}
