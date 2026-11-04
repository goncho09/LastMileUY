using System.Text.Json;
using System.Text.Json.Serialization;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.Contracts.Avisos;

// Versiones del contrato de los avisos a comercios (ver docs/mensajeria.md, "Versiones de los avisos").
// Cada suscripción elige la suya y la mantiene: un comercio que ya recibe avisos no se entera de los cambios
// de una versión nueva hasta que decide pasarse.
// - v1: el formato de siempre (el evento tal cual, campos en PascalCase). Solo eventos de envío.
// - v2: el formato nuevo, con eventos de bulto (entrega parcial). Agrupa los datos del envío y del bulto,
//   en camelCase, y sin identificadores internos (ni ids de envío, operador o comercio).
public static class VersionesAvisos
{
    public const int V1 = 1;
    public const int V2 = 2;

    public static readonly int[] Todas = [V1, V2];

    // Para las suscripciones que no dicen versión: las que ya existían y los comercios que no la piden
    public const int PorDefecto = V1;

    public static bool Existe(int version) => Todas.Contains(version);

    // A qué tipos de evento se puede suscribir cada versión
    public static bool Permite(int version, string tipoEvento) => version switch
    {
        V1 => tipoEvento is TiposEvento.EnvioAdmitido or TiposEvento.EnvioEstadoCambiado,
        V2 => tipoEvento is TiposEvento.EnvioAdmitido or TiposEvento.EnvioEstadoCambiado
            or TiposEvento.BultoEstadoCambiado,
        _ => false
    };

    // El contenido que recibe el comercio, según su versión
    public static string Contenido(int version, EstadoEnvioCambiado evento) => version == V2
        ? JsonSerializer.Serialize(AvisoV2.Desde(evento), OpcionesV2)
        : JsonSerializer.Serialize(evento);

    private static readonly JsonSerializerOptions OpcionesV2 = new(JsonSerializerDefaults.Web);
}

// Contenido de un aviso v2
public record AvisoV2(
    int Version,
    // Identifica el evento (distinto de X-Aviso-Id, que identifica cada aviso)
    Guid EventoId,
    string Tipo,
    DateTime OcurridoEn,
    string Origen,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CorrelacionId,
    AvisoV2Envio Envio,
    // Solo en los eventos de bulto
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

// El envío: en los eventos de bulto va solo el código (el estado del envío llega en su propio evento)
public record AvisoV2Envio(
    string CodigoSeguimiento,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Estado,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EstadoAnterior);

// El bulto: el código que cargó el comercio, su estado y, si no se entregó, el motivo
public record AvisoV2Bulto(
    string Codigo,
    string Estado,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EstadoAnterior,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MotivoNoEntrega);
