namespace LastMileUY.Contracts.Seguimiento;

// Lo que ve cualquiera que tenga el código de seguimiento (GET /api/seguimiento/{codigo}), sin iniciar
// sesión. Sin datos personales: ni el destinatario, ni la calle, ni las observaciones del repartidor.
public record SeguimientoPublicoDto(
    string CodigoSeguimiento,
    string Estado,
    DateTime FechaCreacion,
    DateTime? FechaEntrega,
    string Ciudad,
    string Departamento,
    OperadorPublicoDto Operador,
    IReadOnlyList<EventoPublicoDto> Eventos,
    // Avance de la entrega por bultos (entrega parcial): solo cuántos, nunca códigos ni ids
    AvanceBultosDto? Bultos = null);

// Cuántos bultos tiene el envío y cuántos se entregaron (o volvieron al comercio)
public record AvanceBultosDto(
    int Total,
    int Entregados,
    int Devueltos)
{
    public bool EntregaParcial => Entregados > 0 && Entregados < Total;
}

// Para pintar la página con la identidad visual del operador que hace la entrega
public record OperadorPublicoDto(
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl);

// Un paso del historial: a qué estado pasó y cuándo
public record EventoPublicoDto(
    DateTime FechaHora,
    string Estado);
