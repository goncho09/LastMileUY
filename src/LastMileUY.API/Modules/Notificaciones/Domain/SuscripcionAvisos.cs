using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Notificaciones.Domain;

public class SuscripcionAvisos : IPerteneceAOperador, IPerteneceAComercio
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public string TipoEvento { get; set; } = string.Empty;

    public string UrlDestino { get; set; } = string.Empty;

    public string ClaveFirma { get; set; } = string.Empty;

    public bool Activa { get; set; } = true;

    public int VersionContrato { get; set; } = 1;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<EntregaAviso> EntregasAvisos { get; set; }
        = new List<EntregaAviso>();
}