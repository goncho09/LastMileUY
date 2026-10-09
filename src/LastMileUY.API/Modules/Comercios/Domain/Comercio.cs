namespace LastMileUY.API.Modules.Comercios.Domain;

using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;

public class Comercio
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? RazonSocial { get; set; }

    public string? RUT { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    
    public ICollection<Operador> Operadores { get; set; }
        = new List<Operador>();
    
    public ICollection<Envio> Envios { get; set; } = new List<Envio>();
    
    public ICollection<Usuario> Usuarios { get; set; }
        = new List<Usuario>();
    
    public ICollection<SuscripcionAvisos> SuscripcionesAvisos { get; set; }
        = new List<SuscripcionAvisos>();
    
    public ICollection<Liquidacion> Liquidaciones { get; set; }
        = new List<Liquidacion>();
}