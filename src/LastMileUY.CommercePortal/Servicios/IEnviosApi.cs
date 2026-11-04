using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.CommercePortal.Servicios;

// Alta y listado de los envíos del comercio (POST y GET api/envios)
public interface IEnviosApi
{
    Task<ResultadoAlta> CrearAsync(CrearEnvioRequest request, CancellationToken ct = default);

    // Los envíos del comercio logueado con el operador elegido; la API filtra por comercio y operador
    Task<PaginaDto<EnvioResumenDto>> ListarAsync(int pagina, int tamanioPagina, string? estado, string? buscar, CancellationToken ct = default);

    // El detalle de un envío del comercio, con el resultado de cada bulto; null si no existe o no es suyo
    Task<EnvioDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default);
}

// O se creó el envío, o la API lo rechazó con un mensaje para mostrar
public record ResultadoAlta(EnvioResponse? Envio, string? Error)
{
    public bool Exito => Envio is not null;
}
