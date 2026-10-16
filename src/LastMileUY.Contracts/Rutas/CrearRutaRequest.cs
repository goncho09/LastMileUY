using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Rutas;

// Alta de una hoja de ruta (POST /api/rutas)
public class CrearRutaRequest
{
    [Required]
    [MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    // Día en que sale la ruta (hoy o más adelante)
    public DateOnly Fecha { get; set; }

    [Range(1, int.MaxValue)]
    public int RepartidorId { get; set; }

    [Range(1, int.MaxValue)]
    public int VehiculoId { get; set; }

    // Envíos en el orden en que se van a visitar: el primero es la parada 1.
    // Ver docs/hojas-de-ruta.md (orden de las paradas).
    [MaxLength(100)]
    public List<int> EnvioIds { get; set; } = [];
}
