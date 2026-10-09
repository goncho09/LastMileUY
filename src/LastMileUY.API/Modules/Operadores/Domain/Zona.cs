namespace LastMileUY.API.Modules.Operadores.Domain;

public class Zona
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public bool Activa { get; set; } = true;
    
    public ICollection<FranjaHoraria> FranjasHorarias { get; set; }
        = new List<FranjaHoraria>();
}