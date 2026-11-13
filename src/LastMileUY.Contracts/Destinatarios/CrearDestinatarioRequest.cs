using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Destinatarios;

public class CrearDestinatarioRequest
{
    public int ComercioId { get; set; }

    [Required, MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Apellido { get; set; } = string.Empty;

    [EmailAddress, MaxLength(250)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telefono { get; set; }

    [Required]
    public CrearDireccionRequest Direccion { get; set; } = new();
}
