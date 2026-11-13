namespace LastMileUY.API.Modules.Mensajeria.Domain;

public sealed class CorreoSeguimiento
{
    public Guid EventoId { get; set; }

    public int EnvioId { get; set; }
    public int OperadorId { get; set; }
    public int ComercioId { get; set; }

    public string Estado { get; set; } = "Pendiente";

    public int Intentos { get; set; }

    public DateTime? FechaEnvio { get; set; }
    public string? UltimoError { get; set; }

    public Guid? ReservaId { get; set; }

    public DateTime? ReservaHasta { get; set; }
}