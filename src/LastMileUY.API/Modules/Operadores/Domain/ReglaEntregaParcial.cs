namespace LastMileUY.API.Modules.Operadores.Domain;

// Una versión de las reglas de entrega parcial de un operador. Las versiones no se editan: cada cambio
// publica una nueva (1, 2, 3...). Así siempre se puede saber con qué reglas se evaluó una entrega.
// La base lo hace cumplir: el usuario de la aplicación solo puede leer e insertar en esta tabla.
//
// Qué versión se aplica: la que tenía el operador cuando se despachó la hoja de ruta (Ruta.ReglaEntregaParcialId).
// Ver docs/sincronizacion.md, "Reglas de entrega parcial".
public class ReglaEntregaParcial : IPerteneceAOperador
{
    public const int LargoMaximoMotivo = 500;

    public int Id { get; set; }

    public int OperadorId { get; set; }

    // Correlativa por operador, empieza en 1
    public int Version { get; set; }

    // Si el repartidor puede entregar algunos bultos y dejar otros. Si no, la entrega parcial se rechaza.
    public bool PermiteEntregaParcial { get; set; }

    // Qué pasa con los bultos que quedaron sin entregar
    public DestinoBultosPendientes DestinoBultosPendientes { get; set; }

    // Por qué se publicó esta versión (lo escribe quien la publica)
    public string? Motivo { get; set; }

    // Usuario que la publicó
    public string PublicadaPorId { get; set; } = string.Empty;

    public DateTime FechaPublicacion { get; set; } = DateTime.UtcNow;
}

public enum DestinoBultosPendientes
{
    // Quedan pendientes para salir en otra hoja de ruta
    NuevaHojaDeRuta = 1,

    // Vuelven al comercio
    Devolucion = 2
}
