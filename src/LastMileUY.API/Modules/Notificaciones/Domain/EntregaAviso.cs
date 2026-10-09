namespace LastMileUY.API.Modules.Notificaciones.Domain;

public class EntregaAviso
{
    public int Id { get; set; }

    public int SuscripcionAvisosId { get; set; }

    public int EnvioId { get; set; }
    
    // GUID: ID unico
    public Guid EventoId { get; set; } = Guid.NewGuid();

    public string TipoEvento { get; set; } = string.Empty;

    public string Contenido { get; set; } = string.Empty;

    public EstadoEntregaAviso Estado { get; set; } = EstadoEntregaAviso.Pendiente;

    public int CantidadIntentos { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaUltimoIntento { get; set; }

    public DateTime? FechaEntrega { get; set; }

    public DateTime? FechaProximoIntento { get; set; }

    public string? UltimoError { get; set; }
}