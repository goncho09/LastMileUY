using LastMileUY.API.Modules.Envios.Domain;

namespace LastMileUY.API.Modules.Entregas.Domain;

public class ResultadoBultoIntento
{
    public int Id { get; set; }

    public int IntentoEntregaId { get; set; }

    public IntentoEntrega IntentoEntrega { get; set; } = null!;

    public int BultoId { get; set; }

    public Bulto Bulto { get; set; } = null!;

    public bool Entregado { get; set; }

    public int? MotivoNoEntregaId { get; set; }
}