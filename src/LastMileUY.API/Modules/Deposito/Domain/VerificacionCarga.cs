using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Deposito.Domain;

public class VerificacionCarga : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int RutaId { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public string? ResponsableId { get; set; }

    public string? Observaciones { get; set; }

    public bool Completa { get; set; }

    public string Origen { get; set; } = OrigenesVerificacion.Deposito;

    public List<string> Sobrantes { get; set; } = [];

    public ICollection<VerificacionCargaBulto> Bultos { get; set; } = new List<VerificacionCargaBulto>();
}

public static class OrigenesVerificacion
{
    public const string Deposito = "Deposito";

    public const string Repartidor = "Repartidor";
}

public class VerificacionCargaBulto
{
    public int Id { get; set; }

    public int VerificacionCargaId { get; set; }

    public int BultoId { get; set; }

    public bool Cargado { get; set; }
}
