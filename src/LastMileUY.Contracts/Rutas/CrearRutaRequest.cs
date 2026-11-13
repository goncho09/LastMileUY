using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Rutas;

public class CrearRutaRequest
{
    [Required]
    [MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    public DateOnly Fecha { get; set; }

    [Range(1, int.MaxValue)]
    public int RepartidorId { get; set; }

    [Range(1, int.MaxValue)]
    public int VehiculoId { get; set; }

    [MaxLength(100)]
    public List<int> EnvioIds { get; set; } = [];
}
