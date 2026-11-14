using LastMileUY.Contracts.Envios;

namespace LastMileUY.Contracts.Integraciones;

public class ImportarEnviosRequest
{
    public List<EnvioApiRequest> Envios { get; set; } = [];
}

public class EnvioApiRequest
{
    public string? ReferenciaExterna { get; set; }

    public int DestinatarioId { get; set; }

    public int DireccionId { get; set; }

    public List<CrearBultoRequest> Bultos { get; set; } = [];

    public string? Modalidad { get; set; }

    public int? FranjaHorariaId { get; set; }
}

public static class ResultadosImportacion
{
    public const string Creado = "Creado";

    public const string Existente = "Existente";

    public const string Rechazado = "Rechazado";
}

public record ResultadoImportacionDto(
    int Indice,
    string? ReferenciaExterna,
    string Resultado,
    int? EnvioId,
    string? CodigoSeguimiento,
    string? Mensaje);

public record ImportarEnviosResponse(
    int Creados,
    int Existentes,
    int Rechazados,
    List<ResultadoImportacionDto> Resultados);
