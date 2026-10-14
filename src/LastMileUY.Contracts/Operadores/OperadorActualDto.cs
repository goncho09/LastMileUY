namespace LastMileUY.Contracts.Operadores;

// El operador del usuario logueado: su identidad visual y su configuración (GET /api/operador)
public record OperadorActualDto(
    int Id,
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl,
    IReadOnlyList<ReglaOperativaDto> Reglas);

// Una regla operativa activa del operador (hay como máximo una por modalidad)
public record ReglaOperativaDto(
    string Modalidad,
    string Nombre,
    string? Descripcion,
    int MaximoIntentosEntrega,
    int HorasEntreIntentos,
    int PlazoEntregaHoras,
    int HorasParaIniciarDevolucion,
    bool RequiereFirma,
    bool RequiereFotografia,
    bool RequiereDocumentoReceptor);
