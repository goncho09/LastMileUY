namespace LastMileUY.API.Modules.Envios.Domain;

using System.Security.Cryptography;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;

public class Envio : IPerteneceAOperador
{
    // Sin caracteres que se confundan entre sí (0/O, 1/I/L)
    private const string CaracteresCodigo = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LargoCodigo = 12;

    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int DestinatarioId { get; set; }

    public int DireccionId { get; set; }
    
    // Copia de la dirección al crear el envío: si el destinatario después cambia
    // su dirección, el envío conserva la original. Se carga con AsignarDireccionEntrega.
    public DireccionEntrega DireccionEntrega { get; private set; } = null!;

    // Código aleatorio para el enlace de seguimiento público; el Id nunca se expone
    public string CodigoSeguimiento { get; set; } = GenerarCodigoSeguimiento();

    // Identificador del envío en el sistema del comercio; evita duplicados al reimportar
    public string? ReferenciaExterna { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEntrega { get; set; }

    public decimal? CostoEnvio { get; set; }
    
    // Solo cambia mediante CambiarEstado, que valida la transición
    public EstadoEnvio Estado { get; private set; } = EstadoEnvio.Admitido;
    
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

    // Control de concurrencia optimista (columna xmin de PostgreSQL)
    public uint Version { get; private set; }

    public void CambiarEstado(EstadoEnvio nuevoEstado)
    {
        if (!TransicionesEnvio.EsValida(Estado, nuevoEstado))
        {
            throw new InvalidOperationException(
                $"No se puede pasar el envío de {Estado} a {nuevoEstado}.");
        }

        Estado = nuevoEstado;
    }

    public void AsignarDireccionEntrega(DireccionEntrega direccion)
    {
        ArgumentNullException.ThrowIfNull(direccion);

        DireccionEntrega = direccion;
    }

    public static string GenerarCodigoSeguimiento() =>
        RandomNumberGenerator.GetString(CaracteresCodigo, LargoCodigo);
}