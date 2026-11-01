using System.Text;
using LastMileUY.API.Modules.Mensajeria.Domain;
using LastMileUY.Contracts.Mensajeria;
using RabbitMQ.Client;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public sealed class PublicadorReintentosFallidos(
    ConexionRabbitMq rabbit)
{
    public async Task PublicarAsync(
        MensajeFallido mensaje,
        CancellationToken ct = default)
    {
        await using var canal = await rabbit.CrearCanalAsync(ct);

        var propiedades = new BasicProperties
        {
            MessageId = mensaje.MensajeId,
            CorrelationId = mensaje.CorrelacionId,
            Type = mensaje.Tipo,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await canal.BasicPublishAsync(
            exchange: "",
            routingKey: TopologiaMensajeria.ColaWorker,
            mandatory: true,
            basicProperties: propiedades,
            body: Encoding.UTF8.GetBytes(mensaje.Contenido),
            cancellationToken: ct);
    }
}