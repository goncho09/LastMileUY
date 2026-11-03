namespace LastMileUY.Contracts.Sincronizacion;

// Una fila del historial de sincronización que ve el Backoffice (GET /api/sincronizacion/operaciones)
public record OperacionSincronizadaDto(
    Guid IdOperacion,
    string Tipo,
    string Resultado,
    string Mensaje,
    int RutaId,
    int EnvioId,
    string? CodigoSeguimiento,
    string Repartidor,
    DateTime FechaHoraDispositivo,
    DateTime FechaRecepcion,
    // Si la entrega trajo firma o foto: se descargan de GET /api/sincronizacion/evidencias/{intentoId}/{firma|foto}
    int? IntentoEntregaId,
    bool TieneFirma,
    bool TieneFoto,
    // Versión de las reglas de entrega parcial con la que se evaluó (la de la hoja de ruta); null si no había
    int? VersionReglasEntregaParcial = null);

