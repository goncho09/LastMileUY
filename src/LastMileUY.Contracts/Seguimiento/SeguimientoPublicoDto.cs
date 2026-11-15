namespace LastMileUY.Contracts.Seguimiento;

public record SeguimientoPublicoDto(
    string CodigoSeguimiento,
    string Estado,
    DateTime FechaCreacion,
    DateTime? FechaEntrega,
    string Ciudad,
    string Departamento,
    OperadorPublicoDto Operador,
    IReadOnlyList<EventoPublicoDto> Eventos,
    AvanceBultosDto? Bultos = null,
    ReprogramacionPublicaDto? Reprogramacion = null);

public record AvanceBultosDto(
    int Total,
    int Entregados,
    int Devueltos)
{
    public bool EntregaParcial => Entregados > 0 && Entregados < Total;
}

public record OperadorPublicoDto(
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl);

public record EventoPublicoDto(
    DateTime FechaHora,
    string Estado);

public record FranjaPublicaDto(string Nombre, TimeOnly HoraInicio, TimeOnly HoraFin);

public record ReprogramacionPublicaDto(
    bool Permitida,
    string? Motivo,
    DateOnly FechaMinima,
    DateOnly FechaMaxima,
    IReadOnlyList<FranjaPublicaDto> Franjas,
    DateOnly? FechaSolicitada,
    string? FranjaSolicitada);

public record SolicitarReprogramacionRequest(DateOnly Fecha, string? Franja = null, string? Comentario = null);
