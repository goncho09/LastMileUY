namespace LastMileUY.API.Modules.Entregas.Domain;

public class IntentoEntrega
{
    public int Id { get; set; }

    public int EnvioId { get; set; }

    public int? ParadaId { get; set; }

    public int NumeroIntento { get; set; }

    public DateTime FechaHora { get; set; }

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    public bool Exitoso { get; set; }

    public int? MotivoNoEntregaId { get; set; }

    public string? Observaciones { get; set; }
    
    public PruebaEntrega? PruebaEntrega { get; set; }
}