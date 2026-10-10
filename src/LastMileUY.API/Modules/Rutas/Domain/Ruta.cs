namespace LastMileUY.API.Modules.Rutas.Domain;

using LastMileUY.API.Modules.Operadores.Domain;

public class Ruta : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int RepartidorId { get; set; }

    public int VehiculoId { get; set; }

    public DateOnly Fecha { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public EstadoRuta Estado { get; set; } = EstadoRuta.Planificada;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }
    
    public Rendicion? Rendicion { get; set; }
    
    public ICollection<Parada> Paradas { get; set; }
        = new List<Parada>();
    
    public ICollection<Posicion> Posiciones { get; set; }
        = new List<Posicion>();
    
    public ICollection<Incidencia> Incidencias { get; set; }
        = new List<Incidencia>();
}