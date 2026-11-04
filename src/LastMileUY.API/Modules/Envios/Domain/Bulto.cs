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

    // Cambios de estado todavía sin evento publicado (no se guarda en la base). El OutboxInterceptor
    // los toma al guardar y deja un mensaje por bulto en el outbox, en la misma transacción.
    private readonly List<CambioEstadoBulto> cambiosSinPublicar = [];

    public IReadOnlyList<CambioEstadoBulto> CambiosSinPublicar => cambiosSinPublicar;

    // motivoNoEntregaId: el motivo del catálogo del operador, cuando el bulto no se entregó
    public void CambiarEstado(EstadoBulto nuevoEstado, int? motivoNoEntregaId = null)
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

        cambiosSinPublicar.Add(new CambioEstadoBulto(Estado, nuevoEstado, motivoNoEntregaId));
        Estado = nuevoEstado;
    }

    // Ya quedaron en el outbox
    public void MarcarCambiosPublicados() => cambiosSinPublicar.Clear();

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

// Un cambio de estado de un bulto, para publicar su evento (ver Bulto.CambiosSinPublicar)
public readonly record struct CambioEstadoBulto(EstadoBulto Anterior, EstadoBulto Nuevo, int? MotivoNoEntregaId);
