using LastMileUY.Contracts.Mensajeria;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

// Una sola conexión a RabbitMQ por instancia de la API (las conexiones son caras; los canales no).
// Se abre la primera vez que se usa, así la API arranca aunque RabbitMQ todavía no esté listo.
public sealed class ConexionRabbitMq(string cadenaConexion) : IAsyncDisposable
{
    private readonly SemaphoreSlim candado = new(1, 1);
    private IConnection? conexion;

    // Canal con confirmación de publicación: BasicPublishAsync espera a que RabbitMQ diga que guardó
    // el mensaje, y falla si lo rechaza o si no hay cola que lo reciba (mandatory)
    public async Task<IChannel> CrearCanalAsync(CancellationToken cancellationToken)
    {
        var abierta = await ObtenerConexionAsync(cancellationToken);
        var canal = await abierta.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        // Declarar es idempotente: si ya existe igual, no hace nada
        await canal.ExchangeDeclareAsync(
            TopologiaMensajeria.ExchangeEnvios, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);

        return canal;
    }

    private async Task<IConnection> ObtenerConexionAsync(CancellationToken cancellationToken)
    {
        if (conexion is { IsOpen: true })
            return conexion;

        await candado.WaitAsync(cancellationToken);

        try
        {
            if (conexion is { IsOpen: true })
                return conexion;

            if (conexion is not null)
                await conexion.DisposeAsync();

            var fabrica = new ConnectionFactory
            {
                Uri = new Uri(cadenaConexion),
                ClientProvidedName = "LastMileUY.API",
                // Si se corta, el cliente reconecta solo y recrea los canales
                AutomaticRecoveryEnabled = true
            };

            conexion = await fabrica.CreateConnectionAsync(cancellationToken);
            return conexion;
        }
        finally
        {
            candado.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (conexion is not null)
            await conexion.DisposeAsync();
    }
}
