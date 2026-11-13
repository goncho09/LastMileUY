namespace LastMileUY.API.Modules.Operadores.Domain;

public class FuncionalidadOperador
{
    public int OperadorId { get; set; }

    public string Clave { get; set; } = string.Empty;

    public bool Activa { get; set; }

    public DateTime FechaModificacion { get; set; } = DateTime.UtcNow;

    public string? ModificadaPor { get; set; }
}
