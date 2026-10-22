namespace LastMileUY.Contracts.Cache;

// Una fila del tablero técnico del caché (GET /api/cache/metricas)
public record MetricaCacheDto(
    string Caso,
    string Nombre,
    string Perfil,
    int DuracionSegundos,
    long Aciertos,
    long Fallos,
    double PorcentajeAciertos);
