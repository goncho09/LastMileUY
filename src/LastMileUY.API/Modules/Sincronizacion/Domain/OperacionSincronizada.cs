using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Sincronizacion.Domain;

// Una operación que mandó la app del repartidor (una entrega o un intento fallido) y qué pasó con ella.
// Se guarda siempre, se haya aplicado o no: si la app la reenvía (porque no le llegó la respuesta),
// se devuelve el mismo resultado sin aplicarla de nuevo. También es el historial que ve el Backoffice.
// RutaId y EnvioId son los que mandó la app, sin clave foránea: una operación rechazada
// puede nombrar una ruta o un envío que no existen.
public class OperacionSincronizada : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    // La genera la app al registrar la operación (sin conexión); identifica los reenvíos
    public Guid IdOperacion { get; set; }

    // Usuario del repartidor que la mandó
    public string UsuarioId { get; set; } = string.Empty;

    public TipoOperacion Tipo { get; set; }

    public int RutaId { get; set; }

    public int EnvioId { get; set; }

    public ResultadoOperacion Resultado { get; set; }

    public string Mensaje { get; set; } = string.Empty;

    // Cuándo pasó en el dispositivo y cuándo llegó a la API
    public DateTime FechaHoraDispositivo { get; set; }

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    // El intento de entrega que generó, si se aplicó
    public int? IntentoEntregaId { get; set; }

    public IntentoEntrega? IntentoEntrega { get; set; }
}

public enum TipoOperacion
{
    Entrega = 1,
    IntentoFallido = 2
}

public enum ResultadoOperacion
{
    // Se aplicó: el envío cambió de estado
    Aplicada = 1,

    // No se aplica nunca: le falta algo o no corresponde (por ejemplo, falta la firma que pide la regla)
    Rechazada = 2,

    // El envío ya no está como la app lo vio (otro lo cambió mientras estaba sin conexión).
    // Política: manda el servidor; lo resuelve el despachador (ver docs/sincronizacion.md).
    Conflicto = 3
}
