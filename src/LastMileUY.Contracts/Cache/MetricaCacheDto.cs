namespace LastMileUY.Contracts.Cache;

public record MetricaCacheDto(
    string Caso,
    string Nombre,
    string Perfil,
    int DuracionSegundos,
    long Aciertos,
    long Fallos,
    double PorcentajeAciertos);
