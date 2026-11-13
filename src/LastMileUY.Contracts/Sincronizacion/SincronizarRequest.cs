using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Sincronizacion;

public class SincronizarRequest
{
    [MaxLength(10)]
    public List<OperacionRequest> Operaciones { get; set; } = [];
}

public class OperacionRequest
{
    public Guid IdOperacion { get; set; }

    [Required]
    public string Tipo { get; set; } = string.Empty;

    public int RutaId { get; set; }

    public int EnvioId { get; set; }

    public DateTime FechaHoraDispositivo { get; set; }

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    [MaxLength(1000)]
    public string? Observaciones { get; set; }

    public int? MotivoNoEntregaId { get; set; }

    [MaxLength(150)]
    public string? NombreReceptor { get; set; }

    [MaxLength(50)]
    public string? DocumentoReceptor { get; set; }

    public string? FirmaBase64 { get; set; }

    public string? FotoBase64 { get; set; }

    public List<ResultadoBultoRequest>? ResultadosBultos { get; set; }
}
