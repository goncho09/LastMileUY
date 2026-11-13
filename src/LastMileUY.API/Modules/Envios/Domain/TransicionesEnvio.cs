namespace LastMileUY.API.Modules.Envios.Domain;

public static class TransicionesEnvio
{
    private static readonly Dictionary<EstadoEnvio, EstadoEnvio[]> Permitidas = new()
    {
        [EstadoEnvio.Admitido] = [EstadoEnvio.EnDeposito],
        [EstadoEnvio.EnDeposito] = [EstadoEnvio.AsignadoARuta, EstadoEnvio.Extraviado],
        [EstadoEnvio.AsignadoARuta] = [EstadoEnvio.EnDeposito, EstadoEnvio.EnTransito, EstadoEnvio.Extraviado],
        [EstadoEnvio.EnTransito] = [EstadoEnvio.Entregado, EstadoEnvio.EntregadoParcialmente, EstadoEnvio.NoEntregado, EstadoEnvio.EnDeposito, EstadoEnvio.Extraviado],
        [EstadoEnvio.NoEntregado] = [EstadoEnvio.Reprogramado, EstadoEnvio.EnDevolucion],
        [EstadoEnvio.Reprogramado] = [EstadoEnvio.AsignadoARuta],
        [EstadoEnvio.Entregado] = [EstadoEnvio.EnDevolucion],
        [EstadoEnvio.EntregadoParcialmente] =
        [
            EstadoEnvio.Reprogramado,
            EstadoEnvio.EnDevolucion
        ],
        [EstadoEnvio.EnDevolucion] = [EstadoEnvio.Devuelto, EstadoEnvio.Extraviado],
        [EstadoEnvio.Devuelto] = [],
        [EstadoEnvio.Extraviado] = [],
    };

    public static bool EsValida(EstadoEnvio desde, EstadoEnvio hacia) =>
        Permitidas.TryGetValue(desde, out var destinos) && destinos.Contains(hacia);

    public static IReadOnlyCollection<EstadoEnvio> DestinosDesde(EstadoEnvio desde) =>
        Permitidas.TryGetValue(desde, out var destinos) ? destinos : [];
}
