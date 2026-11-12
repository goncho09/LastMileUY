using LastMileUY.Contracts.Envios;

namespace LastMileUY.Contracts.Integraciones;

// Importación masiva (POST /api/v1/envios/lote). Cada envío necesita su referencia externa: es lo
// que permite reimportar el lote, completo o una parte, sin duplicar envíos
public class ImportarEnviosRequest
{
    public List<EnvioApiRequest> Envios { get; set; } = [];
}

// Un envío de la API pública (de a uno o en un lote): como el alta del portal, sin el comercio,
// que sale de la clave
public class EnvioApiRequest
{
    public string? ReferenciaExterna { get; set; }

    public int DestinatarioId { get; set; }

    public int DireccionId { get; set; }

    public List<CrearBultoRequest> Bultos { get; set; } = [];
}

// Qué pasó con cada fila del lote
public static class ResultadosImportacion
{
    // Se creó ahora
    public const string Creado = "Creado";

    // Ya existía un envío con esa referencia: no se crea otro y se devuelve el que estaba
    public const string Existente = "Existente";

    // Datos inválidos: no se creó; el motivo va en Mensaje
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
