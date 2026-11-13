namespace LastMileUY.Contracts.Sincronizacion;

public record SincronizarResponse(IReadOnlyList<ResultadoOperacionDto> Resultados);

public record ResultadoOperacionDto(
    Guid IdOperacion,
    string Resultado,
    string Mensaje,
    bool Duplicada,
    bool Reintentar,
    string? EstadoEnvio);
