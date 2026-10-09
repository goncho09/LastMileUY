using Microsoft.AspNetCore.Identity;

namespace LastMileUY.API.Modules.Identidad.Domain;

public class Rol : IdentityRole
{
    public string? Descripcion { get; set; }
}