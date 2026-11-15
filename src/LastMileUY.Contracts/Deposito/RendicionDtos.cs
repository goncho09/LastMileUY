namespace LastMileUY.Contracts.Deposito;

public static class EstadosRendicion
{
    public const string SinRendir = "SinRendir";
    public const string Declarada = "Declarada";
    public const string Confirmada = "Confirmada";
}

public record RutaPorRendirDto(
    int Id,
    string Nombre,
    DateOnly Fecha,
    string Repartidor,
    string EstadoRuta,
    int CantidadEntregados,
    int CantidadADevolver,
    string EstadoRendicion);

public record BultoADevolverDto(
    string Codigo,
    int EnvioId,
    string CodigoSeguimiento,
    string Destinatario,
    string EstadoEnvio,
    bool Declarado,
    bool? Recibido);

public record EnvioRendidoDto(
    int EnvioId,
    string CodigoSeguimiento,
    string EstadoAnterior,
    string EstadoNuevo);

public record RendicionRutaDto(
    int RutaId,
    string Nombre,
    DateOnly Fecha,
    string EstadoRuta,
    string Repartidor,
    string EstadoRendicion,
    int CantidadEntregados,
    int CantidadNoEntregados,
    IReadOnlyList<BultoADevolverDto> BultosADevolver)
{
    public DateTime? DeclaradaEn { get; init; }

    public string? ObservacionesRepartidor { get; init; }

    public DateTime? ConfirmadaEn { get; init; }

    public string? Observaciones { get; init; }

    public IReadOnlyList<string> Faltantes { get; init; } = [];

    public IReadOnlyList<string> Sobrantes { get; init; } = [];

    public IReadOnlyList<EnvioRendidoDto> Envios { get; init; } = [];

    public bool SePuedeConfirmar => EstadoRendicion != EstadosRendicion.Confirmada
        && EstadoRuta is "Despachada" or "EnCurso" or "Finalizada";
}

public record DeclararRendicionRequest(
    IReadOnlyList<string> BultosDevueltos,
    string? Observaciones = null,
    DateTime? DeclaradaEn = null);

public record ConfirmarRendicionRequest(
    IReadOnlyList<string> BultosRecibidos,
    string? Observaciones = null);
