using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Reportes.Domain;

public class IndicadorDiario : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public DateOnly Dia { get; set; }

    public int EnviosCreados { get; set; }

    public int Entregados { get; set; }

    public int EntregadosATiempo { get; set; }

    public int NoEntregados { get; set; }

    public int IntentosFallidos { get; set; }

    public decimal Importe { get; set; }

    public bool Cerrado { get; set; }

    public DateTime CalculadoEn { get; set; }
}
