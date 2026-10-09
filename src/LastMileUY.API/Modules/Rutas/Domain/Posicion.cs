namespace LastMileUY.API.Modules.Rutas.Domain;

public class Posicion
{
    public int Id { get; set; }

    public int RutaId { get; set; }

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    public DateTime FechaHora { get; set; }

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;
}