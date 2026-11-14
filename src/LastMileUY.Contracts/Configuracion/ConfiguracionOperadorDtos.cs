namespace LastMileUY.Contracts.Configuracion;

public record CambiarActivoRequest(bool Activo);

public record ZonaDto(
    int Id,
    string Nombre,
    string? Descripcion,
    bool Activa,
    IReadOnlyList<FranjaHorariaDto> Franjas)
{
    public string? Departamento { get; init; }

    public IReadOnlyList<string> Localidades { get; init; } = [];
}

public record FranjaHorariaDto(
    int Id,
    int ZonaId,
    string Nombre,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    bool Activa);

public record GuardarZonaRequest(
    string Nombre,
    string? Descripcion,
    string? Departamento = null,
    IReadOnlyList<string>? Localidades = null);

public record GuardarFranjaHorariaRequest(string Nombre, TimeOnly HoraInicio, TimeOnly HoraFin);

public record VehiculoConfiguracionDto(
    int Id,
    string Matricula,
    string Tipo,
    decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3,
    bool Activo);

public record GuardarVehiculoRequest(
    string Matricula,
    string Tipo,
    decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3);

public record MotivoNoEntregaConfiguracionDto(
    int Id,
    string Nombre,
    string? Descripcion,
    bool Activo);

public record GuardarMotivoNoEntregaRequest(string Nombre, string? Descripcion);

public record ReglaOperativaConfiguracionDto(
    int Id,
    string Nombre,
    string? Descripcion,
    string Modalidad,
    int MaximoIntentosEntrega,
    int HorasEntreIntentos,
    int PlazoEntregaHoras,
    int HorasParaIniciarDevolucion,
    bool RequiereFirma,
    bool RequiereFotografia,
    bool RequiereDocumentoReceptor,
    bool Activa)
{
    public int EnviosAsociados { get; init; }
}

public record GuardarReglaOperativaRequest(
    string Nombre,
    string? Descripcion,
    string Modalidad,
    int MaximoIntentosEntrega,
    int HorasEntreIntentos,
    int PlazoEntregaHoras,
    int HorasParaIniciarDevolucion,
    bool RequiereFirma,
    bool RequiereFotografia,
    bool RequiereDocumentoReceptor);

public record CuadroTarifarioDto(
    int Id,
    int Version,
    DateOnly VigenciaDesde,
    DateOnly? VigenciaHasta,
    string Estado,
    IReadOnlyList<TarifaDto> Tarifas)
{
    public bool Editable => Estado == EstadosCuadroTarifario.Programado;
}

public static class EstadosCuadroTarifario
{
    public const string Programado = "Programado";
    public const string Vigente = "Vigente";
    public const string Vencido = "Vencido";
}

public record TarifaDto(
    int Id,
    string Nombre,
    int ZonaId,
    string Zona,
    string Modalidad,
    decimal PesoMinimoKg,
    decimal PesoMaximoKg,
    decimal VolumenMinimoM3,
    decimal VolumenMaximoM3,
    decimal PrecioBase,
    decimal PrecioPorKgAdicional,
    bool Activa);

public record CrearCuadroTarifarioRequest(DateOnly VigenciaDesde, bool CopiarTarifasDeLaAnterior);

public record GuardarTarifaRequest(
    string Nombre,
    int ZonaId,
    string Modalidad,
    decimal PesoMinimoKg,
    decimal PesoMaximoKg,
    decimal VolumenMinimoM3,
    decimal VolumenMaximoM3,
    decimal PrecioBase,
    decimal PrecioPorKgAdicional);
