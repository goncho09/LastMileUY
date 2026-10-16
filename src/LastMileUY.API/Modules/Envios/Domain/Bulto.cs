namespace LastMileUY.API.Modules.Envios.Domain;

public class Bulto
{
    public int Id { get; set; }

    public int EnvioId { get; set; }

    public string Codigo { get; set; } = string.Empty;

    public decimal PesoKg { get; set; }

    public decimal LargoCm { get; set; }

    public decimal AnchoCm { get; set; }

    public decimal AltoCm { get; set; }

    public string? Descripcion { get; set; }

    public const decimal CentimetrosCubicosPorMetroCubico = 1_000_000m;

    // Lo que ocupa en el vehículo (no es columna: sale de las medidas)
    public decimal VolumenM3 => LargoCm * AnchoCm * AltoCm / CentimetrosCubicosPorMetroCubico;
}