namespace LastMileUY.Contracts.Sincronizacion;

// Un resultado por operación, en el mismo orden en que llegaron
public record SincronizarResponse(IReadOnlyList<ResultadoOperacionDto> Resultados);

// Resultado: "Aplicada", "Rechazada", "Conflicto" o "Error".
// Duplicada: la operación ya había llegado antes; se devuelve el resultado de esa vez sin aplicarla de nuevo.
// Reintentar: solo en "Error" (un problema momentáneo); la app la vuelve a mandar con el mismo IdOperacion.
// EstadoEnvio: cómo quedó el envío en el servidor (null si el envío no existe para este repartidor).
public record ResultadoOperacionDto(
    Guid IdOperacion,
    string Resultado,
    string Mensaje,
    bool Duplicada,
    bool Reintentar,
    string? EstadoEnvio);
