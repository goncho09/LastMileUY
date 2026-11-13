using LastMileUY.Contracts.Mensajeria;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public sealed class ConexionRabbitMq(string cadenaConexion) : IAsyncDisposable
{
    private readonly SemaphoreSlim candado = new(1, 1);
    private IConnection? conexion;

    public async Task<IChannel> CrearCanalAsync(CancellationToken cancellationToken)
    {
        var abierta = await ObtenerConexionAsync(cancellationToken);
        var canal = await abierta.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

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
