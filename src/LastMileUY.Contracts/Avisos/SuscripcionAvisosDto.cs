namespace LastMileUY.Contracts.Avisos;

// Una suscripción del comercio a avisos: a qué evento y a qué URL (GET /api/avisos/suscripciones)
public record SuscripcionAvisosDto(
    int Id,
    string TipoEvento,
    string UrlDestino,
    bool Activa,
    DateTime FechaCreacion);

// Alta de una suscripción (POST /api/avisos/suscripciones)
public class CrearSuscripcionAvisosRequest
{
    public string TipoEvento { get; set; } = string.Empty;

    public string UrlDestino { get; set; } = string.Empty;
}

// Activar o desactivar una suscripción (PATCH /api/avisos/suscripciones/{id})
public record CambiarSuscripcionAvisosRequest(bool Activa);
