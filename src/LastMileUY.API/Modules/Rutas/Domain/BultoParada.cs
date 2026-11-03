namespace LastMileUY.API.Modules.Rutas.Domain;

// Qué bultos de su envío lleva una parada (el manifiesto de la hoja de ruta). Se fija al armar la ruta,
// con los bultos que todavía no se entregaron: si el envío ya tuvo una entrega parcial, la ruta nueva
// lleva solo los que faltan. Así la hoja muestra siempre lo que salió, también después de terminada.
public class BultoParada
{
    public int ParadaId { get; set; }

    public int BultoId { get; set; }
}
