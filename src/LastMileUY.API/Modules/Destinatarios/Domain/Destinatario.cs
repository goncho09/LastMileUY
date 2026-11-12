using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Envios.Domain;

namespace LastMileUY.API.Modules.Destinatarios.Domain;

public class Destinatario : IPerteneceAComercio
{
    public int Id { get; set; }

    // El destinatario pertenece al comercio que lo cargó
    public int ComercioId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Apellido { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // Cargado con una clave del ambiente de pruebas de la API pública (docs/api-publica.md): solo se ve
    // en ese ambiente, y los de verdad no se ven ahí. Lo pone ApplicationDbContext al crearlo, según
    // quién lo crea, y no se puede cambiar después
    public bool EsPrueba { get; set; }

    public ICollection<Direccion> Direcciones { get; set; }
        = new List<Direccion>();

    public ICollection<Envio> Envios { get; set; } = new List<Envio>();
}