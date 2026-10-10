namespace LastMileUY.API.Modules.Envios.Domain;

// Transiciones válidas del envío. Cualquier cambio que no esté acá se rechaza.
// Los números corresponden a la tabla de transiciones de la documentación.
public static class TransicionesEnvio
{
    private static readonly Dictionary<EstadoEnvio, EstadoEnvio[]> Permitidas = new()
    {
        // 1
        [EstadoEnvio.Admitido] = [EstadoEnvio.EnDeposito],
        // 2, 13
        [EstadoEnvio.EnDeposito] = [EstadoEnvio.AsignadoARuta, EstadoEnvio.Extraviado],
        // 3, 4, 13
        [EstadoEnvio.AsignadoARuta] = [EstadoEnvio.EnDeposito, EstadoEnvio.EnTransito, EstadoEnvio.Extraviado],
        // 5, 6, 7, 13
        [EstadoEnvio.EnTransito] = [EstadoEnvio.Entregado, EstadoEnvio.NoEntregado, EstadoEnvio.EnDeposito, EstadoEnvio.Extraviado],
        // 8, 9
        [EstadoEnvio.NoEntregado] = [EstadoEnvio.Reprogramado, EstadoEnvio.EnDevolucion],
        // 10
        [EstadoEnvio.Reprogramado] = [EstadoEnvio.AsignadoARuta],
        // 11
        [EstadoEnvio.Entregado] = [EstadoEnvio.EnDevolucion],
        // 12, 13
        [EstadoEnvio.EnDevolucion] = [EstadoEnvio.Devuelto, EstadoEnvio.Extraviado],
        [EstadoEnvio.Devuelto] = [],
        [EstadoEnvio.Extraviado] = [],
    };

    public static bool EsValida(EstadoEnvio desde, EstadoEnvio hacia) =>
        Permitidas.TryGetValue(desde, out var destinos) && destinos.Contains(hacia);

    public static IReadOnlyCollection<EstadoEnvio> DestinosDesde(EstadoEnvio desde) =>
        Permitidas.TryGetValue(desde, out var destinos) ? destinos : [];
}
