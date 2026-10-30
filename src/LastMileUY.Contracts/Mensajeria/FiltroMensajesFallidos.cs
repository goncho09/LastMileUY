namespace LastMileUY.Contracts.Mensajeria;

public record FiltroMensajesFallidos(
    int Pagina = 1,
    int TamanoPagina = 20,
    string? Estado = null,
    DateTime? Desde = null,
    DateTime? Hasta = null
);