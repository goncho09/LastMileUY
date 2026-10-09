namespace LastMileUY.API.Modules.Operadores.Domain;

using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;

public class Operador
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    
    public ICollection<ReglaOperativa> ReglasOperativas { get; set; }
        = new List<ReglaOperativa>();
    
    public ICollection<CuadroTarifario> CuadrosTarifarios { get; set; }
        = new List<CuadroTarifario>();
    
    public ICollection<Zona> Zonas { get; set; } = new List<Zona>();
    
    public ICollection<Comercio> Comercios { get; set; }
        = new List<Comercio>();
    
    public ICollection<Envio> Envios { get; set; } = new List<Envio>();
    
    public ICollection<MotivoNoEntrega> MotivosNoEntrega { get; set; }
        = new List<MotivoNoEntrega>();
    
    public ICollection<Repartidor> Repartidores { get; set; }
        = new List<Repartidor>();
    
    public ICollection<Vehiculo> Vehiculos { get; set; }
        = new List<Vehiculo>();
    
    public ICollection<Ruta> Rutas { get; set; }
        = new List<Ruta>();
    
    public ICollection<Usuario> Usuarios { get; set; }
        = new List<Usuario>();
    
    public ICollection<SuscripcionAvisos> SuscripcionesAvisos { get; set; }
        = new List<SuscripcionAvisos>();
    
    public ICollection<Liquidacion> Liquidaciones { get; set; }
        = new List<Liquidacion>();
}