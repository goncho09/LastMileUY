namespace LastMileUY.API.Modules.Entregas.Domain;

public class Devolucion
{
    public int Id { get; set; }

    public int EnvioId { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

    public DateTime? FechaFinalizacion { get; set; }

    public string? Observaciones { get; set; }
}