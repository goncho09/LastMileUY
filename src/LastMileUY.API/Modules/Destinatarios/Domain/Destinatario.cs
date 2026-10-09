namespace LastMileUY.API.Modules.Destinatarios.Domain;

using LastMileUY.API.Modules.Envios.Domain;

public class Destinatario
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Apellido { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<Direccion> Direcciones { get; set; }
        = new List<Direccion>();
    
    public ICollection<Envio> Envios { get; set; } = new List<Envio>();
}