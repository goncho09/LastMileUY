using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Envios;

// Un bulto (paquete) del envío al crearlo. El código lo genera la API.
public class CrearBultoRequest
{
    [Range(0.01, 1000)]
    public decimal PesoKg { get; set; }

    [Range(0.1, 1000)]
    public decimal LargoCm { get; set; }

    [Range(0.1, 1000)]
    public decimal AnchoCm { get; set; }

    [Range(0.1, 1000)]
    public decimal AltoCm { get; set; }

    [MaxLength(500)]
    public string? Descripcion { get; set; }
}
