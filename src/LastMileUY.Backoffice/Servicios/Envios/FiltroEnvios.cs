namespace LastMileUY.Backoffice.Servicios;

public record FiltroEnvios(
    string? Estado = null,
    string? Buscar = null,
    int Pagina = 1,
    int TamanioPagina = 20,
    string? Canal = null);
