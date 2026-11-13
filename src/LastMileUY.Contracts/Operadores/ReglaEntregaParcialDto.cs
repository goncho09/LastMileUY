using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Operadores;

public record ReglaEntregaParcialDto(
    int Id,
    int Version,
    bool PermiteEntregaParcial,
    string DestinoBultosPendientes,
    decimal RecargoNuevaVisita,
    string? Motivo,
    string PublicadaPor,
    DateTime FechaPublicacion);

public class PublicarReglaEntregaParcialRequest
{
    public bool PermiteEntregaParcial { get; set; }

    [Required]
    public string DestinoBultosPendientes { get; set; } = string.Empty;

    [Range(0, 99999999.99)]
    public decimal RecargoNuevaVisita { get; set; } = 0;

    [Required]
    [MaxLength(500)]
    public string Motivo { get; set; } = string.Empty;
}
