namespace LastMileUY.Contracts.Comun;

public record PaginaDto<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Pagina,
    int TamanioPagina)
{
    public int TotalPaginas =>
        TamanioPagina <= 0 ? 0 : (int)Math.Ceiling(Total / (double)TamanioPagina);
}
