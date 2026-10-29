namespace LastMileUY.API.Modules.Notificaciones.Domain;

// Historial de entregas: una fila por cada vez que el worker intentó mandar un aviso al comercio.
// EntregaAviso guarda el estado actual (y el último error); acá queda cada intento, para el panel
// del comercio (letra 6.9). Es hija de EntregaAviso: se ve solo si se ve el aviso (ADR-01).
public class IntentoAviso
{
    public int Id { get; set; }

    public int EntregaAvisoId { get; set; }

    // 1 la primera vez (es el X-Aviso-Intento que recibió el comercio)
    public int NumeroIntento { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    // El comercio respondió 2xx
    public bool Exitoso { get; set; }

    // null si el comercio no llegó a responder (caído, tiempo agotado, destino no permitido)
    public int? CodigoHttp { get; set; }

    public string? Error { get; set; }

    // Cuánto tardó el comercio en responder
    public int DuracionMs { get; set; }
}
