namespace LastMileUY.Contracts.Comun;

// Una página de resultados de un listado
public record PaginaDto<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Pagina,
    int TamanioPagina)
{
    public int TotalPaginas =>
        TamanioPagina <= 0 ? 0 : (int)Math.Ceiling(Total / (double)TamanioPagina);
}
