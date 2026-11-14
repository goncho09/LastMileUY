namespace LastMileUY.Contracts.Deposito;

public static class EstadosCarga
{
    public const string SinVerificar = "SinVerificar";
    public const string Incompleta = "Incompleta";
    public const string Verificada = "Verificada";
}

public record RutaPorCargarDto(
    int Id,
    string Nombre,
    DateOnly Fecha,
    string Repartidor,
    string Vehiculo,
    int CantidadEnvios,
    int CantidadBultos,
    string EstadoCarga);

public record BultoACargarDto(
    string Codigo,
    int EnvioId,
    string CodigoSeguimiento,
    int OrdenParada,
    string Destinatario,
    string? Descripcion,
    bool? Cargado);

public record CargaRutaDto(
    int RutaId,
    string Nombre,
    DateOnly Fecha,
    string Estado,
    string Repartidor,
    string Vehiculo,
    string EstadoCarga,
    DateTime? UltimaVerificacion,
    IReadOnlyList<BultoACargarDto> Bultos)
{
    public bool SePuedeVerificar => Estado == "Planificada";
}

public record VerificarCargaRequest(IReadOnlyList<string> BultosCargados, string? Observaciones = null);
