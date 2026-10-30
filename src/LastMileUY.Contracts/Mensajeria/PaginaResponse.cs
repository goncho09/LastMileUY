namespace LastMileUY.Contracts.Mensajeria;

public record PaginaResponse<T>(
    IReadOnlyList<T> Items,
    int Pagina,
    int TamanoPagina,
    int Total
);