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
    AvanceBultosDto? Bultos = null);

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
