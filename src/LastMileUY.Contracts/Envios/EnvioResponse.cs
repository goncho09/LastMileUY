namespace LastMileUY.Contracts.Envios;

public class EnvioResponse
{
    public int Id { get; set; }

    public string CodigoSeguimiento { get; set; } = string.Empty;

    public int ComercioId { get; set; }

    public int DestinatarioId { get; set; }

    public string Estado { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; }
}