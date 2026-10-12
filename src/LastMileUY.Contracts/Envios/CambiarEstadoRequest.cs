using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Envios;

// PATCH /api/envios/{id}/estado. Estado: nombre del estado nuevo (por ejemplo "EnDeposito").
public class CambiarEstadoRequest
{
    [Required]
    public string Estado { get; set; } = string.Empty;

    // Comentario que queda en el historial (opcional)
    [MaxLength(1000)]
    public string? Descripcion { get; set; }
}
