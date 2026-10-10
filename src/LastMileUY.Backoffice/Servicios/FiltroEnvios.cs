namespace LastMileUY.Backoffice.Servicios;

// Lo que el usuario elige en el listado: estado, texto a buscar y página
public record FiltroEnvios(
    string? Estado = null,
    string? Buscar = null,
    int Pagina = 1,
    int TamanioPagina = 20);
