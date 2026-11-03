namespace LastMileUY.API.Modules.Rutas.Domain;

public class Parada
{
    public int Id { get; set; }

    public int RutaId { get; set; }

    public int EnvioId { get; set; }

    public int Orden { get; set; }

    public DateTime? FechaLlegada { get; set; }

    public DateTime? FechaSalida { get; set; }

    public string? Observaciones { get; set; }

    // Los bultos que lleva (ver BultoParada). Vacío en las rutas armadas antes de que existiera:
    // en ese caso se toman todos los bultos del envío.
    public ICollection<BultoParada> Bultos { get; set; } = new List<BultoParada>();
}