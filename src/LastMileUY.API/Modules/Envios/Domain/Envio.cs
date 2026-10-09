namespace LastMileUY.API.Modules.Envios.Domain;

using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;

public class Envio
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int DestinatarioId { get; set; }

    public int DireccionId { get; set; }
    
    public DireccionEntrega DireccionEntrega { get; private set; } = null!;

    public string CodigoSeguimiento { get; set; } = string.Empty;

    public string ReferenciaExterna { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEntrega { get; set; }

    public decimal? CostoEnvio { get; set; }
    
    public EstadoEnvio Estado { get; set; } = EstadoEnvio.Admitido;
    
    public ModalidadEnvio Modalidad { get; set; } = ModalidadEnvio.Estandar;

    public int? FranjaHorariaId { get; set; }
    
    public Devolucion? Devolucion { get; set; }
    
    public int? CuadroTarifarioId { get; set; }
    
    public ICollection<Bulto> Bultos { get; set; } = new List<Bulto>();
    
    public ICollection<EventoEnvio> Eventos { get; set; }
        = new List<EventoEnvio>();
    
    public ICollection<IntentoEntrega> IntentosEntrega { get; set; }
        = new List<IntentoEntrega>();
    
    public ICollection<Parada> Paradas { get; set; }
        = new List<Parada>();
    
    public ICollection<Notificacion> Notificaciones { get; set; }
        = new List<Notificacion>();
    
    public ICollection<EntregaAviso> EntregasAvisos { get; set; }
        = new List<EntregaAviso>();
    
    public ICollection<DetalleLiquidacion> DetallesLiquidacion { get; set; }
        = new List<DetalleLiquidacion>();
}