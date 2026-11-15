using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.CommercePortal.Servicios;

public interface IEnviosApi
{
    Task<ResultadoAlta> CrearAsync(CrearEnvioRequest request, CancellationToken ct = default);

    Task<PaginaDto<EnvioResumenDto>> ListarAsync(int pagina, int tamanioPagina, string? estado, string? buscar, CancellationToken ct = default);

    Task<EnvioDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default);

    Task<OpcionesEntregaDto?> OpcionesEntregaAsync(int direccionId, CancellationToken ct = default);
    Task<ResultadoImportacion> ImportarAsync(string contenido, string? nombreArchivo, CancellationToken ct = default);

}

public record ResultadoAlta(EnvioResponse? Envio, string? Error)
{
    public bool Exito => Envio is not null;
}

public record ResultadoImportacion(LastMileUY.Contracts.Integraciones.ImportarEnviosResponse? Respuesta, string? Error);

public interface ICuentaApi
{
    Task<LastMileUY.Contracts.Reportes.ReporteGestionDto?> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}

public interface IDevolucionesApi
{
    Task<List<LastMileUY.Contracts.Envios.DevolucionComercioDto>> ListarAsync(bool soloPendientes, CancellationToken ct = default);

    Task<string?> ConfirmarRecibidaAsync(int envioId, CancellationToken ct = default);
}
