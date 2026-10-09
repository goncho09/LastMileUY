namespace LastMileUY.API.Modules.Comercios.Domain;

public class OperadorComercio
{
    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public DateTime FechaVinculacion { get; set; } = DateTime.UtcNow;

    public bool Activo { get; set; } = true;
}