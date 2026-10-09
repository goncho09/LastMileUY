namespace LastMileUY.API.Modules.Notificaciones.Domain;

public class Notificacion
{
    public int Id { get; set; }

    public int EnvioId { get; set; }

    public string Canal { get; set; } = string.Empty;

    public string Destino { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public EstadoNotificacion Estado { get; set; } = EstadoNotificacion.Pendiente;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEnvio { get; set; }

    public string? Error { get; set; }
}