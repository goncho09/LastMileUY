namespace LastMileUY.API.Modules.Rutas.Domain;

public class Incidencia
{
    public int Id { get; set; }

    public int RutaId { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public bool Resuelta { get; set; } = false;

    public DateTime? FechaResolucion { get; set; }

    public string? ObservacionesResolucion { get; set; }
}