namespace LastMileUY.API.Modules.Entregas.Domain;

public class PruebaEntrega
{
    public int Id { get; set; }

    public int IntentoEntregaId { get; set; }

    public string? FirmaArchivoKey { get; set; }

    public string? FotoArchivoKey { get; set; }

    public string? NombreReceptor { get; set; }

    public string? DocumentoReceptor { get; set; }

    public string? Observaciones { get; set; }

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    public DateTime FechaHoraDispositivo { get; set; }

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;
}