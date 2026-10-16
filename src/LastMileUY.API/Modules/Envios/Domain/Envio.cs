namespace LastMileUY.API.Modules.Envios.Domain;

using System.Security.Cryptography;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;

public class Envio : IPerteneceAOperador, IPerteneceAComercio
{
    // Sin caracteres que se confundan entre sí (0/O, 1/I/L)
    private const string CaracteresCodigo = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LargoCodigo = 12;

    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int DestinatarioId { get; set; }
    
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

    // Por dónde entró (portal del comercio o API): el Backoffice lo muestra y permite filtrarlo
    public CanalIngreso Canal { get; set; } = CanalIngreso.Api;

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

    // Intentos de entrega que fallaron (necesita IntentosEntrega cargados)
    public int IntentosFallidos => IntentosEntrega.Count(i => !i.Exitoso);

    // Estados a los que se puede pasar desde el actual: la tabla de transiciones,
    // recortada por la regla operativa del operador (si tiene una para la modalidad del envío).
    public IReadOnlyList<EstadoEnvio> EstadosSiguientes(ReglaOperativa? regla = null) =>
        TransicionesEnvio.DestinosDesde(Estado)
            .Where(destino => MotivoDeRechazo(destino, regla) is null)
            .ToList();

    // Cambia el estado si la transición es válida y lo deja registrado en el historial.
    // origen: desde dónde se hizo (Backoffice, Repartidor, Sistema...); responsableId: el usuario.
    // regla: la regla operativa vigente del operador; sin regla solo aplica la tabla de transiciones.
    // Para pasar a Entregado o NoEntregado hay que tener cargados los IntentosEntrega.
    // ocurrido y ubicacion: cuándo y dónde pasó, si no es ahora (la app del repartidor registra
    // sin conexión y sincroniza después; ver SincronizarEntregasService).
    // Devuelve el intento de entrega que registró, si el cambio fue a Entregado o NoEntregado.
    public IntentoEntrega? CambiarEstado(
        EstadoEnvio nuevoEstado,
        string origen = "Sistema",
        string? descripcion = null,
        string? responsableId = null,
        ReglaOperativa? regla = null,
        DateTime? ocurrido = null,
        (decimal Latitud, decimal Longitud)? ubicacion = null)
    {
        var motivo = MotivoDeRechazo(nuevoEstado, regla);

        if (motivo is not null)
        {
            throw new InvalidOperationException(motivo);
        }

        var cuando = ocurrido ?? DateTime.UtcNow;
        IntentoEntrega? intento = null;

        // Llegar a Entregado o NoEntregado es el resultado de un intento de entrega
        if (nuevoEstado is EstadoEnvio.Entregado or EstadoEnvio.NoEntregado)
        {
            intento = new IntentoEntrega
            {
                NumeroIntento = IntentosEntrega.Count + 1,
                FechaHora = cuando,
                Exitoso = nuevoEstado == EstadoEnvio.Entregado,
                Observaciones = descripcion
            };

            IntentosEntrega.Add(intento);
        }

        Estado = nuevoEstado;

        if (nuevoEstado == EstadoEnvio.Entregado)
        {
            FechaEntrega = cuando;
        }

        RegistrarEvento(origen, descripcion, responsableId, cuando, ubicacion);

        return intento;
    }

    // null si se puede pasar a ese estado; si no, el motivo para mostrarle al usuario
    private string? MotivoDeRechazo(EstadoEnvio nuevoEstado, ReglaOperativa? regla)
    {
        if (!TransicionesEnvio.EsValida(Estado, nuevoEstado))
        {
            return $"No se puede pasar el envío de {Estado} a {nuevoEstado}.";
        }

        // Agotados los intentos que permite el operador, el envío ya no se reprograma: va a devolución
        if (nuevoEstado == EstadoEnvio.Reprogramado
            && regla is not null
            && IntentosFallidos >= regla.MaximoIntentosEntrega)
        {
            return $"El envío ya tuvo {IntentosFallidos} intentos fallidos y la regla \"{regla.Nombre}\" "
                + $"permite {regla.MaximoIntentosEntrega}: tiene que pasar a devolución.";
        }

        return null;
    }

    // Primer evento del historial: el envío quedó admitido
    public void RegistrarAdmision(string origen, string? responsableId = null) =>
        RegistrarEvento(origen, "Envío admitido", responsableId, DateTime.UtcNow, null);

    private void RegistrarEvento(
        string origen,
        string? descripcion,
        string? responsableId,
        DateTime cuando,
        (decimal Latitud, decimal Longitud)? ubicacion) =>
        Eventos.Add(new EventoEnvio
        {
            Tipo = Estado.ToString(),
            Descripcion = descripcion,
            Origen = origen,
            ResponsableId = responsableId,
            FechaHora = cuando,
            Latitud = ubicacion?.Latitud,
            Longitud = ubicacion?.Longitud
        });

    public void AsignarDireccionEntrega(DireccionEntrega direccion)
    {
        ArgumentNullException.ThrowIfNull(direccion);

        DireccionEntrega = direccion;
    }

    public static string GenerarCodigoSeguimiento() =>
        RandomNumberGenerator.GetString(CaracteresCodigo, LargoCodigo);
}