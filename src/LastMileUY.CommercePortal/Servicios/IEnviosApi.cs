using LastMileUY.Contracts.Envios;

namespace LastMileUY.CommercePortal.Servicios;

// Alta de envíos desde el portal (POST api/envios)
public interface IEnviosApi
{
    Task<ResultadoAlta> CrearAsync(CrearEnvioRequest request, CancellationToken ct = default);
}

// O se creó el envío, o la API lo rechazó con un mensaje para mostrar
public record ResultadoAlta(EnvioResponse? Envio, string? Error)
{
    public bool Exito => Envio is not null;
}
