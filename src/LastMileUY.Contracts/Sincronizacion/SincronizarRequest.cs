using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Sincronizacion;

// Lo que manda la app del repartidor al recuperar conexión (POST /api/sincronizacion):
// las operaciones que registró sin conexión, en el orden en que pasaron.
// Reglas y política de conflictos: docs/sincronizacion.md
public class SincronizarRequest
{
    // Pocas por lote: cada una puede traer una firma y una foto, y el pedido entero
    // tiene que entrar en el límite de 30 MB del servidor. Más operaciones van en otro lote.
    [MaxLength(10)]
    public List<OperacionRequest> Operaciones { get; set; } = [];
}

public class OperacionRequest
{
    // La genera la app al registrar la operación y la repite en cada reenvío
    public Guid IdOperacion { get; set; }

    // "Entrega" o "IntentoFallido"
    [Required]
    public string Tipo { get; set; } = string.Empty;

    public int RutaId { get; set; }

    public int EnvioId { get; set; }

    // Cuándo pasó en el dispositivo (UTC)
    public DateTime FechaHoraDispositivo { get; set; }

    // Dónde pasó
    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    [MaxLength(1000)]
    public string? Observaciones { get; set; }

    // Intento fallido: por qué no se pudo entregar (GET /api/sincronizacion/motivos)
    public int? MotivoNoEntregaId { get; set; }

    // Entrega: evidencia. Qué es obligatorio lo dice la regla operativa del operador
    // (firma, foto, documento del receptor), que viene en la hoja de ruta.
    [MaxLength(150)]
    public string? NombreReceptor { get; set; }

    [MaxLength(50)]
    public string? DocumentoReceptor { get; set; }

    // Imágenes PNG o JPEG en base64, de hasta 1 MB cada una
    public string? FirmaBase64 { get; set; }

    public string? FotoBase64 { get; set; }
}
