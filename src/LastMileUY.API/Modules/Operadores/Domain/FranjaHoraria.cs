namespace LastMileUY.API.Modules.Operadores.Domain;

using LastMileUY.API.Modules.Envios.Domain;

public class FranjaHoraria
{
    public int Id { get; set; }

    public int ZonaId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }

    public bool Activa { get; set; } = true;
    
    public ICollection<Envio> Envios { get; set; }
        = new List<Envio>();
}