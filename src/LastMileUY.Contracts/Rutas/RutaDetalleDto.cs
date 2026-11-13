using LastMileUY.Contracts.Envios;

namespace LastMileUY.Contracts.Rutas;

public record RutaDetalleDto(
    int Id,
    string Nombre,
    DateOnly Fecha,
    string Estado,
    RepartidorDto Repartidor,
    VehiculoDto Vehiculo,
    decimal PesoTotalKg,
    decimal VolumenTotalM3,
    IReadOnlyList<ParadaDto> Paradas,
    ReglaEntregaParcialRutaDto? ReglaEntregaParcial = null);

public record RepartidorDto(
    int Id,
    string Nombre);

public record VehiculoDto(
    int Id,
    string Matricula,
    string Tipo,
    decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3);

public record ParadaDto(
    int Orden,
    int EnvioId,
    string CodigoSeguimiento,
    string EstadoEnvio,
    DestinatarioDto Destinatario,
    DireccionDto Direccion,
    FranjaHorariaDto? Franja,
    int CantidadBultos,
    decimal PesoKg,
    decimal VolumenM3,
    string Modalidad,
    EvidenciaRequeridaDto Evidencia,
    IReadOnlyList<BultoRutaDto>? Bultos = null);

public record EvidenciaRequeridaDto(
    bool Firma,
    bool Foto,
    bool DocumentoReceptor)
{
    public static readonly EvidenciaRequeridaDto Ninguna = new(false, false, false);
}

public record FranjaHorariaDto(
    string Nombre,
    TimeOnly HoraInicio,
    TimeOnly HoraFin);

public record BultoRutaDto(
    int Id,
    string Codigo,
    string Estado,
    string? Descripcion,
    decimal PesoKg);

public record ReglaEntregaParcialRutaDto(
    int Version,
    bool PermiteEntregaParcial,
    string DestinoBultosPendientes);
