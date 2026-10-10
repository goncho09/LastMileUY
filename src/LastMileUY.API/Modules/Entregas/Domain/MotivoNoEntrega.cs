namespace LastMileUY.API.Modules.Entregas.Domain;

using LastMileUY.API.Modules.Operadores.Domain;

public class MotivoNoEntrega : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public ICollection<IntentoEntrega> IntentosEntrega { get; set; }
        = new List<IntentoEntrega>();
}