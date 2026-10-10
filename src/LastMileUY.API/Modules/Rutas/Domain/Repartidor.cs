namespace LastMileUY.API.Modules.Rutas.Domain;

using LastMileUY.API.Modules.Operadores.Domain;

public class Repartidor : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public string UsuarioId { get; set; } = string.Empty;

    public string? LicenciaConducir { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    
    public ICollection<Ruta> Rutas { get; set; }
        = new List<Ruta>();
}