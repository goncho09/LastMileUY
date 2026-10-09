namespace LastMileUY.API.Modules.Tarifas.Domain;

public class DetalleLiquidacion
{
    public int Id { get; set; }

    public int LiquidacionId { get; set; }

    public int EnvioId { get; set; }

    public decimal Importe { get; set; }
}