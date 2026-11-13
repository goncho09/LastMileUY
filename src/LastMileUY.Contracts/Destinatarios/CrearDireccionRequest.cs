using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Destinatarios;

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

    [Required, MaxLength(100)]
    public string Departamento { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? CodigoPostal { get; set; }

    [MaxLength(300)]
    public string? Referencia { get; set; }

    [Range(-90, 90)]
    public decimal? Latitud { get; set; }

    [Range(-180, 180)]
    public decimal? Longitud { get; set; }
}
