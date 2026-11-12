using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Integraciones.Domain;

// Clave de la API pública (docs/api-publica.md): con ella el sistema del comercio le habla a la API
// sin iniciar sesión. Vale para un comercio y un operador, igual que la sesión del portal después de
// elegir operador, así que el filtro de EF y el RLS (ADR-01) la aíslan como a cualquier dato del comercio.
// De la clave se guarda solo el hash: si alguien lee la tabla no puede usarla.
public class ClaveApi : IPerteneceAOperador, IPerteneceAComercio
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    // Para que el comercio sepa dónde la usa ("Tienda online", "ERP")
    public string Nombre { get; set; } = string.Empty;

    // El comienzo de la clave, visible en el portal para reconocerla (no alcanza para usarla)
    public string Prefijo { get; set; } = string.Empty;

    // SHA-256 de la clave completa, en hexadecimal
    public string Hash { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // Usuario del comercio que la creó
    public string CreadaPorId { get; set; } = string.Empty;

    // Se actualiza como mucho una vez por minuto, para no escribir en cada pedido
    public DateTime? UltimoUso { get; set; }

    // Revocada = deja de funcionar en el pedido siguiente. No se borra: queda el registro
    public DateTime? FechaRevocacion { get; set; }
}
