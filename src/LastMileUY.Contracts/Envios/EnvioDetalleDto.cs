namespace LastMileUY.Contracts.Envios;

// Todo lo que se muestra de un envío (GET /api/envios/{id})
public record EnvioDetalleDto(
    int Id,
    string CodigoSeguimiento,
    string? ReferenciaExterna,
    string Comercio,
    string Estado,
    string Modalidad,
    DateTime FechaCreacion,
    DateTime? FechaEntrega,
    decimal? CostoEnvio,
    DestinatarioDto Destinatario,
    DireccionDto Direccion,
    IReadOnlyList<BultoDto> Bultos,
    IReadOnlyList<EventoEnvioDto> Eventos)
{
    // Estados a los que se puede pasar desde el actual (para el formulario de cambio de estado).
    // Ya tiene en cuenta la regla operativa: agotados los intentos, no ofrece Reprogramado.
    public IReadOnlyList<string> EstadosSiguientes { get; init; } = [];

    // Regla operativa del operador para la modalidad del envío (null si no definió ninguna)
    public ReglaAplicadaDto? Regla { get; init; }

    public int IntentosFallidos { get; init; }

    // Fecha de creación + plazo de la regla (null sin regla)
    public DateTime? FechaLimiteEntrega { get; init; }
}

public record ReglaAplicadaDto(
    string Nombre,
    int MaximoIntentosEntrega,
    int HorasEntreIntentos,
    int PlazoEntregaHoras,
    bool RequiereFirma,
    bool RequiereFotografia,
    bool RequiereDocumentoReceptor);

public record DestinatarioDto(
    string Nombre,
    string Apellido,
    string? Telefono,
    string? Email);

public record DireccionDto(
    string Calle,
    string Numero,
    string? Apartamento,
    string Ciudad,
    string Departamento,
    string? CodigoPostal,
    string? Referencia);

public record BultoDto(
    string Codigo,
    decimal PesoKg,
    decimal LargoCm,
    decimal AnchoCm,
    decimal AltoCm,
    string? Descripcion);

public record EventoEnvioDto(
    DateTime FechaHora,
    string Tipo,
    string? Descripcion,
    string Origen);
