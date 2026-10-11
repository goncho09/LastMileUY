using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Envios;

public class CrearEnvioRequest
{
    [Range(1, int.MaxValue)]
    public int ComercioId { get; set; }

    [Range(1, int.MaxValue)]
    public int DestinatarioId { get; set; }

    [Range(1, int.MaxValue)]
    public int DireccionId { get; set; }

    [MaxLength(100)]
    public string? ReferenciaExterna { get; set; }
}