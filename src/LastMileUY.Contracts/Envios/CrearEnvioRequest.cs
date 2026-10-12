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

    // Paquetes del envío (opcional). El código de cada bulto lo genera la API.
    [MaxLength(50)]
    public List<CrearBultoRequest> Bultos { get; set; } = [];
}