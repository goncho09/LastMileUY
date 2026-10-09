using Microsoft.AspNetCore.Identity;

namespace LastMileUY.API.Modules.Identidad.Domain;

public class Usuario : IdentityUser
{
    public int? OperadorId { get; set; }

    public int? ComercioId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Apellido { get; set; } = string.Empty;

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}