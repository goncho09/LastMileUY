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
    IReadOnlyList<EventoPublicoDto> Eventos);

// Para pintar la página con la identidad visual del operador que hace la entrega
public record OperadorPublicoDto(
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl);

// Un paso del historial: a qué estado pasó y cuándo
public record EventoPublicoDto(
    DateTime FechaHora,
    string Estado);
