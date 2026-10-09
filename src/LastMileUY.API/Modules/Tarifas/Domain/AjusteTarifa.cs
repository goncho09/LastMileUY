namespace LastMileUY.API.Modules.Tarifas.Domain;

public class AjusteTarifa
{
    public int Id { get; set; }

    public int TarifaId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public TipoAjusteTarifa Tipo { get; set; }

    public decimal Porcentaje { get; set; }

    public bool Activo { get; set; } = true;
}