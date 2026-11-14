namespace LastMileUY.API.Modules.Deposito.Domain;

public class RecepcionBulto
{
    public const decimal ToleranciaPesoRelativa = 0.10m;

    public const decimal ToleranciaPesoMinimaKg = 0.1m;

    public int Id { get; set; }

    public int RecepcionDepositoId { get; set; }

    public int BultoId { get; set; }

    public EstadoBultoRecibido Estado { get; set; }

    public decimal PesoDeclaradoKg { get; set; }

    public decimal? PesoMedidoKg { get; set; }

    public bool PesoDistinto =>
        PesoMedidoKg is { } medido
        && Math.Abs(medido - PesoDeclaradoKg) > Math.Max(ToleranciaPesoMinimaKg, PesoDeclaradoKg * ToleranciaPesoRelativa);

    public bool TieneDiscrepancia => Estado != EstadoBultoRecibido.Recibido || PesoDistinto;
}
