using System.Text.Json.Serialization;

namespace LastMileUY.Contracts.Mensajeria;

public record EstadoEnvioCambiado(
    Guid EventoId,
    string Tipo,
    int Version,
    DateTime OcurridoEn,
    int OperadorId,
    int ComercioId,
    int EnvioId,
    string CodigoSeguimiento,
    string Estado,
    string? EstadoAnterior,
    string Origen,
    bool EsPublico,
    string? CorrelacionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CodigoBulto = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MotivoNoEntrega = null)
{
    public const int VersionActual = 1;
}

public static class TiposEvento
{
    public const string EnvioAdmitido = "envio.admitido";
    public const string EnvioEstadoCambiado = "envio.estado_cambiado";

    public const string BultoEstadoCambiado = "bulto.estado_cambiado";
}
