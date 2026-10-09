namespace LastMileUY.API.Modules.Rutas.Domain;

public class Rendicion
{
    public int Id { get; set; }

    public int RutaId { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public int CantidadEntregados { get; set; }

    public int CantidadNoEntregados { get; set; }
    
    public string? Observaciones { get; set; }
}