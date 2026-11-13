using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Envios;

public class CambiarEstadoRequest
{
    [Required]
    public string Estado { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Descripcion { get; set; }
}
