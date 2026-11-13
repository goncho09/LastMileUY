namespace LastMileUY.Contracts.Rutas;

public class SugerirOrdenRequest
{
    public List<int> EnvioIds { get; set; } = [];
}

public record SugerenciaOrdenDto(
    IReadOnlyList<int> EnvioIds,
    double KmActual,
    double KmSugerido,
    IReadOnlyList<string> SinUbicacion,
    bool DesdeDeposito);
