namespace LastMileUY.Contracts.Deposito;

public static class EstadosBultoRecibido
{
    public const string Recibido = "Recibido";
    public const string Danado = "Danado";
    public const string Faltante = "Faltante";
}

public record EnvioPorRecibirDto(
    int Id,
    string CodigoSeguimiento,
    string Comercio,
    string Destinatario,
    DateTime FechaCreacion,
    int CantidadBultos,
    bool TuvoRecepcionIncompleta);

public record BultoPorRecibirDto(
    int Id,
    string Codigo,
    decimal PesoKg,
    decimal LargoCm,
    decimal AnchoCm,
    decimal AltoCm,
    string? Descripcion);

public record EnvioParaRecibirDto(
    int Id,
    string CodigoSeguimiento,
    string Estado,
    string Comercio,
    string Destinatario,
    IReadOnlyList<BultoPorRecibirDto> Bultos,
    IReadOnlyList<RecepcionDto> Recepciones)
{
    public bool SePuedeRecibir => Estado == "Admitido";
}

public record BultoRecibidoRequest(string Codigo, string Estado, decimal? PesoMedidoKg = null);

public record RegistrarRecepcionRequest(int EnvioId, IReadOnlyList<BultoRecibidoRequest> Bultos, string? Observaciones = null);

public record BultoRecibidoDto(
    string Codigo,
    string Estado,
    decimal PesoDeclaradoKg,
    decimal? PesoMedidoKg,
    bool PesoDistinto);

public record RecepcionDto(
    int Id,
    int EnvioId,
    string CodigoSeguimiento,
    string Comercio,
    DateTime FechaHora,
    bool Aceptada,
    bool TieneDiscrepancias,
    string? Observaciones,
    IReadOnlyList<BultoRecibidoDto> Bultos,
    DateTime? FechaResolucion,
    string? Resolucion)
{
    public bool Pendiente => TieneDiscrepancias && FechaResolucion is null;
}

public record ResolverDiscrepanciaRequest(string Resolucion);
