namespace LastMileUY.Backoffice.Servicios;

// Lo que el usuario elige en el listado: estado, texto a buscar, página y canal (Portal o Api)
public record FiltroEnvios(
    string? Estado = null,
    string? Buscar = null,
    int Pagina = 1,
    int TamanioPagina = 20,
    string? Canal = null);
