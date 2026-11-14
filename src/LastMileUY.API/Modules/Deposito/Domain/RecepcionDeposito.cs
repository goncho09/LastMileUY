using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Deposito.Domain;

public class RecepcionDeposito : IPerteneceAOperador, IPerteneceAComercio
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int EnvioId { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public string? ResponsableId { get; set; }

    public string? Observaciones { get; set; }

    public bool Aceptada { get; set; }

    public bool TieneDiscrepancias { get; set; }

    public DateTime? FechaResolucion { get; set; }

    public string? Resolucion { get; set; }

    public string? ResueltaPor { get; set; }

    public bool Pendiente => TieneDiscrepancias && FechaResolucion is null;

    public ICollection<RecepcionBulto> Bultos { get; set; } = new List<RecepcionBulto>();

    public void Resolver(string resolucion, string? responsableId, DateTime cuando)
    {
        if (!Pendiente)
            throw new InvalidOperationException("Esta recepción no tiene discrepancias pendientes.");

        Resolucion = resolucion;
        ResueltaPor = responsableId;
        FechaResolucion = cuando;
    }
}
