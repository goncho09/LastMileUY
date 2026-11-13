using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Infrastructure.Mensajeria;

public class MensajeProcesado : IPerteneceAOperador
{
    public Guid MensajeId { get; set; }

    public string Consumidor { get; set; } = string.Empty;

    public int OperadorId { get; set; }

    public DateTime FechaProcesado { get; set; }
}
