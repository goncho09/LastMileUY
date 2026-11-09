using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Destinatarios;

// Un destinatario nuevo con su primera dirección (POST api/destinatarios).
// Sin dirección no se le podría mandar ningún envío, así que va junto con el alta.
public class CrearDestinatarioRequest
{
    // El usuario de un comercio no lo manda (o manda el suyo); el operador elige uno de sus comercios
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
