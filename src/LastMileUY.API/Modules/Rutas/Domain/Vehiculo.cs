namespace LastMileUY.API.Modules.Rutas.Domain;

public class Vehiculo
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public string Matricula { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;

    public decimal CapacidadPesoKg { get; set; }

    public decimal CapacidadVolumenM3 { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    
    public ICollection<Ruta> Rutas { get; set; }
        = new List<Ruta>();
}