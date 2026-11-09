using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Destinatarios;

// Una dirección de entrega nueva (POST api/destinatarios y POST api/destinatarios/{id}/direcciones).
// Los límites son los de la base (DireccionConfiguration); la API los valida y contesta 400.
public class CrearDireccionRequest
{
    [Required, MaxLength(150)]
    public string Calle { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Numero { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Apartamento { get; set; }

    [Required, MaxLength(100)]
    public string Ciudad { get; set; } = string.Empty;

    // Uno de los 19 departamentos de Uruguay
    [Required, MaxLength(100)]
    public string Departamento { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? CodigoPostal { get; set; }

    // Indicaciones para el repartidor: "portón verde", "timbre 2"...
    [MaxLength(300)]
    public string? Referencia { get; set; }

    // Dónde queda en el mapa (grados decimales). Opcional, pero van las dos o ninguna:
    // sin ubicación el envío no entra en el orden sugerido de la hoja de ruta
    [Range(-90, 90)]
    public decimal? Latitud { get; set; }

    [Range(-180, 180)]
    public decimal? Longitud { get; set; }
}
