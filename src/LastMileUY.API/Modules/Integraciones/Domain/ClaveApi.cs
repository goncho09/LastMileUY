using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Integraciones.Domain;

public class ClaveApi : IPerteneceAOperador, IPerteneceAComercio
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Prefijo { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public string CreadaPorId { get; set; } = string.Empty;

    public bool EsPrueba { get; set; }

    public DateTime? UltimoUso { get; set; }

    public DateTime? FechaRevocacion { get; set; }
}
