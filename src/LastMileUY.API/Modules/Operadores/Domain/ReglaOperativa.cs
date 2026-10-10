namespace LastMileUY.API.Modules.Operadores.Domain;

using LastMileUY.API.Modules.Envios.Domain;

public class ReglaOperativa : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public int MaximoIntentosEntrega { get; set; }

    public int HorasEntreIntentos { get; set; }

    public bool RequiereFirma { get; set; }

    public bool RequiereFotografia { get; set; }

    public bool RequiereDocumentoReceptor { get; set; }

    public int HorasParaIniciarDevolucion { get; set; }
    
    public ModalidadEnvio Modalidad { get; set; }

    public int PlazoEntregaHoras { get; set; }

    public bool Activa { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}