namespace LastMileUY.API.Modules.Envios.Domain;

public class Bulto
{
    public int Id { get; set; }

    public int EnvioId { get; set; }

    public string Codigo { get; set; } = string.Empty;

    public decimal PesoKg { get; set; }

    public decimal LargoCm { get; set; }

    public decimal AnchoCm { get; set; }

    public decimal AltoCm { get; set; }

    public string? Descripcion { get; set; }

    // Cada bulto tiene su propio estado de entrega.
    public EstadoBulto Estado { get; private set; } = EstadoBulto.Pendiente;

    public const decimal CentimetrosCubicosPorMetroCubico = 1_000_000m;

    public decimal VolumenM3 =>
        LargoCm * AnchoCm * AltoCm / CentimetrosCubicosPorMetroCubico;

    public void CambiarEstado(EstadoBulto nuevoEstado)
    {
        if (Estado == nuevoEstado)
        {
            throw new InvalidOperationException(
                $"El bulto ya está en estado {nuevoEstado}.");
        }

        if (!EsTransicionValida(nuevoEstado))
        {
            throw new InvalidOperationException(
                $"No se puede pasar el bulto de {Estado} a {nuevoEstado}.");
        }

        Estado = nuevoEstado;
    }

    private bool EsTransicionValida(EstadoBulto nuevoEstado) =>
        Estado switch
        {
            EstadoBulto.Pendiente =>
                nuevoEstado is EstadoBulto.Entregado
                    or EstadoBulto.NoEntregado
                    or EstadoBulto.Devuelto
                    or EstadoBulto.Extraviado,

            EstadoBulto.NoEntregado =>
                nuevoEstado is EstadoBulto.Pendiente
                    or EstadoBulto.Devuelto
                    or EstadoBulto.Extraviado,

            _ => false
        };
}