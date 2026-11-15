using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Seguimiento.Domain;

public class SolicitudReprogramacion : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int EnvioId { get; set; }

    public DateOnly FechaSolicitada { get; set; }

    public int? FranjaHorariaId { get; set; }

    public string? Comentario { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
