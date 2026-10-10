namespace LastMileUY.API.Modules.Tarifas.Domain;

using LastMileUY.API.Modules.Operadores.Domain;

public class Liquidacion : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public DateOnly PeriodoDesde { get; set; }

    public DateOnly PeriodoHasta { get; set; }

    public decimal ImporteTotal { get; set; }

    public EstadoLiquidacion Estado { get; set; } = EstadoLiquidacion.Pendiente;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaCierre { get; set; }

    public string? Observaciones { get; set; }
    
    public ICollection<DetalleLiquidacion> Detalles { get; set; }
        = new List<DetalleLiquidacion>();
}