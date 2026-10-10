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
    IReadOnlyList<EventoEnvioDto> Eventos);

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
