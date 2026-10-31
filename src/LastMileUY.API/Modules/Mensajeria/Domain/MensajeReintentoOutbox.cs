namespace LastMileUY.API.Modules.Mensajeria.Domain;

public sealed class MensajeReintentoOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MensajeFallidoId { get; set; }

    public string MensajeId { get; set; } = string.Empty;

    public string Contenido { get; set; } = string.Empty;

    public string? Tipo { get; set; }

    public string? CorrelacionId { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaPublicacion { get; set; }

    public int IntentosPublicacion { get; set; }

    public string? UltimoError { get; set; }

    public MensajeFallido MensajeFallido { get; set; } = null!;
}