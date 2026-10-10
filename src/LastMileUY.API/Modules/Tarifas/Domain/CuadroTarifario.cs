namespace LastMileUY.API.Modules.Tarifas.Domain;

using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Envios.Domain;

public class CuadroTarifario : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int Version { get; set; }

    public DateTime VigenciaDesde { get; set; }

    public DateTime? VigenciaHasta { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

	public ICollection<Tarifa> Tarifas { get; set; } = new List<Tarifa>();
	
	public ICollection<Envio> Envios { get; set; }
		= new List<Envio>();
}