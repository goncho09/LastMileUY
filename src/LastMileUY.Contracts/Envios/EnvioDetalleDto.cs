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

    // Por dónde entró: "Portal" (portal del comercio) o "Api"
    public string Canal { get; init; } = "Api";

    // Fecha de creación + plazo de la regla (null sin regla)
    public DateTime? FechaLimiteEntrega { get; init; }

    // Los intentos de entrega, del primero al último, con el resultado de cada bulto y la prueba
    public IReadOnlyList<IntentoEntregaDto> Intentos { get; init; } = [];
}

// Un intento de entrega. Resultado: "Entregado", "Parcial" o "NoEntregado".
// Bultos: el resultado de cada bulto que participó (vacío en intentos de antes del resultado por bulto).
public record IntentoEntregaDto(
    int Id,
    int NumeroIntento,
    DateTime FechaHora,
    string Resultado,
    string? Motivo,
    string? Observaciones,
    IReadOnlyList<ResultadoBultoDto> Bultos,
    PruebaEntregaDto? Prueba);

// Qué pasó con un bulto en un intento. Motivo: por qué no se entregó.
public record ResultadoBultoDto(string Codigo, bool Entregado, string? Motivo);

// La prueba de entrega de un intento. Respalda solo los bultos que se entregaron en ese intento
// (BultosEntregados); vacío si el intento es de antes del resultado por bulto.
// La firma y la foto las descarga el Backoffice (GET /api/sincronizacion/evidencias/{intentoId}/{firma|foto}).
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
    // Pendiente, Entregado, NoEntregado, Devuelto o Extraviado
    public string Estado { get; init; } = "Pendiente";
}

public record EventoEnvioDto(
    DateTime FechaHora,
    string Tipo,
    string? Descripcion,
    string Origen);
