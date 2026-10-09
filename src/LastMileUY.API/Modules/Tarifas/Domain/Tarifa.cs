namespace LastMileUY.API.Modules.Tarifas.Domain;

using LastMileUY.API.Modules.Envios.Domain;

public class Tarifa
{
    public int Id { get; set; }

    public int CuadroTarifarioId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public int ZonaId { get; set; }

    public ModalidadEnvio Modalidad { get; set; }

    public decimal PesoMinimoKg { get; set; }

    public decimal PesoMaximoKg { get; set; }

    public decimal VolumenMinimoM3 { get; set; }

    public decimal VolumenMaximoM3 { get; set; }

    public decimal PrecioBase { get; set; }

    public decimal PrecioPorKgAdicional { get; set; }

    public bool Activa { get; set; } = true;
    
    public ICollection<AjusteTarifa> Ajustes { get; set; }
        = new List<AjusteTarifa>();
}