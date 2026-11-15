namespace LastMileUY.API.Modules.Rutas.Domain;

public class Rendicion
{
    public int Id { get; set; }

    public int RutaId { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public int CantidadEntregados { get; set; }

    public int CantidadNoEntregados { get; set; }

    public string? Observaciones { get; set; }

    public DateTime? DeclaradaEn { get; set; }

    public string? ObservacionesRepartidor { get; set; }

    public List<string> BultosDeclarados { get; set; } = [];

    public DateTime? ConfirmadaEn { get; set; }

    public string? ConfirmadaPor { get; set; }

    public List<string> BultosRecibidos { get; set; } = [];

    public List<string> Faltantes { get; set; } = [];

    public List<string> Sobrantes { get; set; } = [];

    public bool Confirmada => ConfirmadaEn is not null;
}
