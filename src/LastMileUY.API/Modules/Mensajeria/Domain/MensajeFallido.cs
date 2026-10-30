namespace LastMileUY.API.Modules.Mensajeria.Domain;

public class MensajeFallido
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string MensajeId { get; set; } = string.Empty;
    public string? CorrelacionId { get; set; }
    public string? Tipo { get; set; }

    public string Contenido { get; set; } = string.Empty;
    public string? UltimoError { get; set; }

    public int Intentos { get; set; }

    public DateTime FechaFallo { get; set; } = DateTime.UtcNow;

    public EstadoMensajeFallido Estado { get; set; }
        = EstadoMensajeFallido.Pendiente;

    public DateTime? FechaSolicitudReproceso { get; set; }
    public string? SolicitadoPorId { get; set; }

    public DateTime? FechaResolucion { get; set; }
}

public enum EstadoMensajeFallido
{
    Pendiente,
    ReprocesamientoSolicitado,
    Reprocesando,
    Resuelto
}