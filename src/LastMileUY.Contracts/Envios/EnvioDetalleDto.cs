namespace LastMileUY.Contracts.Envios;

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
    public IReadOnlyList<string> EstadosSiguientes { get; init; } = [];

    public ReglaAplicadaDto? Regla { get; init; }

    public int IntentosFallidos { get; init; }

    public string Canal { get; init; } = "Api";

    public DateTime? FechaLimiteEntrega { get; init; }

    public string? Zona { get; init; }

    public FranjaEntregaDto? Franja { get; init; }

    public IReadOnlyList<IntentoEntregaDto> Intentos { get; init; } = [];
}

public record IntentoEntregaDto(
    int Id,
    int NumeroIntento,
    DateTime FechaHora,
    string Resultado,
    string? Motivo,
    string? Observaciones,
    IReadOnlyList<ResultadoBultoDto> Bultos,
    PruebaEntregaDto? Prueba);

public record ResultadoBultoDto(string Codigo, bool Entregado, string? Motivo);

public record PruebaEntregaDto(
    string? NombreReceptor,
    bool TieneFirma,
    bool TieneFoto,
    IReadOnlyList<string> BultosEntregados);

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
    string? Descripcion)
{
    public string Estado { get; init; } = "Pendiente";
}

public record EventoEnvioDto(
    DateTime FechaHora,
    string Tipo,
    string? Descripcion,
    string Origen);
