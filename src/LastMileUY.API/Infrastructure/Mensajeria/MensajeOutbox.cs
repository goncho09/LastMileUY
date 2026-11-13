using LastMileUY.API.Modules.Envios.Domain;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public class MensajeOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Tipo { get; set; } = string.Empty;

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int EnvioId { get; set; }

    public Envio Envio { get; set; } = null!;

    public string CodigoSeguimiento { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public string? EstadoAnterior { get; set; }

    public string Origen { get; set; } = string.Empty;

    public bool EsPublico { get; set; }

    public DateTime OcurridoEn { get; set; }

    public string? CorrelacionId { get; set; }

    public string? CodigoBulto { get; set; }

    public string? MotivoNoEntrega { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaPublicacion { get; set; }

    public int Intentos { get; set; }

    public string? UltimoError { get; set; }
}
