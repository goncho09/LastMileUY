using System.Text.Json;
using System.Text.Json.Serialization;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.Contracts.Avisos;

public static class VersionesAvisos
{
    public const int V1 = 1;
    public const int V2 = 2;

    public static readonly int[] Todas = [V1, V2];

    public const int PorDefecto = V1;

    public static bool Existe(int version) => Todas.Contains(version);

    public static bool Permite(int version, string tipoEvento) => version switch
    {
        V1 => tipoEvento is TiposEvento.EnvioAdmitido or TiposEvento.EnvioEstadoCambiado,
        V2 => tipoEvento is TiposEvento.EnvioAdmitido or TiposEvento.EnvioEstadoCambiado
            or TiposEvento.BultoEstadoCambiado,
        _ => false
    };

    public static string Contenido(int version, EstadoEnvioCambiado evento) => version == V2
        ? JsonSerializer.Serialize(AvisoV2.Desde(evento), OpcionesV2)
        : JsonSerializer.Serialize(evento);

    private static readonly JsonSerializerOptions OpcionesV2 = new(JsonSerializerDefaults.Web);
}

public record AvisoV2(
    int Version,
    Guid EventoId,
    string Tipo,
    DateTime OcurridoEn,
    string Origen,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CorrelacionId,
    AvisoV2Envio Envio,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AvisoV2Bulto? Bulto)
{
    public static AvisoV2 Desde(EstadoEnvioCambiado evento)
    {
        var esDeBulto = evento.Tipo == TiposEvento.BultoEstadoCambiado;

        return new AvisoV2(
            VersionesAvisos.V2,
            evento.EventoId,
            evento.Tipo,
            evento.OcurridoEn,
            evento.Origen,
            evento.CorrelacionId,
            esDeBulto
                ? new AvisoV2Envio(evento.CodigoSeguimiento, null, null)
                : new AvisoV2Envio(evento.CodigoSeguimiento, evento.Estado, evento.EstadoAnterior),
            esDeBulto
                ? new AvisoV2Bulto(evento.CodigoBulto ?? string.Empty, evento.Estado, evento.EstadoAnterior,
                    evento.MotivoNoEntrega)
                : null);
    }
}

public record AvisoV2Envio(
    string CodigoSeguimiento,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Estado,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EstadoAnterior);

public record AvisoV2Bulto(
    string Codigo,
    string Estado,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EstadoAnterior,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MotivoNoEntrega);
