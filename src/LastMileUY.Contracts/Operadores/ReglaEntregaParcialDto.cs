using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Operadores;

// Una versión de las reglas de entrega parcial del operador (GET /api/operador/reglas-entrega-parcial).
// Las versiones no se editan: cada cambio publica una nueva.
public record ReglaEntregaParcialDto(
    int Id,
    int Version,
    bool PermiteEntregaParcial,
    // "NuevaHojaDeRuta" o "Devolucion"
    string DestinoBultosPendientes,
    decimal RecargoNuevaVisita,
    string? Motivo,
    string PublicadaPor,
    DateTime FechaPublicacion);

// Publicar una versión nueva (POST /api/operador/reglas-entrega-parcial). Solo el perfil Operador.
public class PublicarReglaEntregaParcialRequest
{
    public bool PermiteEntregaParcial { get; set; }

    // "NuevaHojaDeRuta" o "Devolucion"
    [Required]
    public string DestinoBultosPendientes { get; set; } = string.Empty;

    [Range(0, 99999999.99)]
    public decimal RecargoNuevaVisita { get; set; } = 0;

    // Por qué se cambian las reglas: queda en el historial
    [Required]
    [MaxLength(500)]
    public string Motivo { get; set; } = string.Empty;
}
