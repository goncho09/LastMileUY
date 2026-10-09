namespace LastMileUY.API.Modules.Seguimiento.Domain;

public class EventoEnvio
{
    public int Id { get; set; }

    public int EnvioId { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public bool EsPublico { get; set; } = true;

    public string Origen { get; set; } = string.Empty;

    public string? ResponsableId { get; set; }

    public decimal? Latitud { get; set; }

    public decimal? Longitud { get; set; }
}