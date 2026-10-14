using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Auth;

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // Solo para usuarios de comercio: con qué operador va a trabajar en esta sesión.
    // Si el comercio trabaja con un único operador se puede omitir (la API lo elige sola).
    public int? OperadorId { get; set; }
}
