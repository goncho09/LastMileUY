namespace LastMileUY.API.Modules.Operadores.Domain;

public class ReglaEntregaParcial : IPerteneceAOperador
{
    public const int LargoMaximoMotivo = 500;

    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int Version { get; set; }

    public bool PermiteEntregaParcial { get; set; }

    public DestinoBultosPendientes DestinoBultosPendientes { get; set; }

    public decimal RecargoNuevaVisita { get; set; }

    public string? Motivo { get; set; }

    public string PublicadaPorId { get; set; } = string.Empty;

    public DateTime FechaPublicacion { get; set; } = DateTime.UtcNow;
}

public enum DestinoBultosPendientes
{
    NuevaHojaDeRuta = 1,

    Devolucion = 2
}
