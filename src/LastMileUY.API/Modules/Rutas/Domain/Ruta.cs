using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Rutas.Domain;

public class Ruta : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int RepartidorId { get; set; }

    public int VehiculoId { get; set; }

    public DateOnly Fecha { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public EstadoRuta Estado { get; set; } = EstadoRuta.Planificada;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public int? ReglaEntregaParcialId { get; set; }

    public ReglaEntregaParcial? ReglaEntregaParcial { get; set; }

    public Rendicion? Rendicion { get; set; }

    public ICollection<Parada> Paradas { get; set; }
        = new List<Parada>();

    public ICollection<Posicion> Posiciones { get; set; }
        = new List<Posicion>();

    public ICollection<Incidencia> Incidencias { get; set; }
        = new List<Incidencia>();

    public static readonly EstadoRuta[] EstadosAbiertos =
        [EstadoRuta.Planificada, EstadoRuta.Despachada, EstadoRuta.EnCurso];

    public static readonly EstadoRuta[] EstadosParaElRepartidor =
        [EstadoRuta.Despachada, EstadoRuta.EnCurso];

    public void Despachar(ReglaEntregaParcial? reglaEntregaParcial = null)
    {
        if (Estado != EstadoRuta.Planificada)
        {
            throw new InvalidOperationException(
                $"Solo se puede despachar una ruta planificada (esta está {Estado}).");
        }

        if (Paradas.Count == 0)
        {
            throw new InvalidOperationException("La ruta no tiene paradas.");
        }

        Estado = EstadoRuta.Despachada;
        ReglaEntregaParcialId = reglaEntregaParcial?.Id;
        ReglaEntregaParcial = reglaEntregaParcial;
    }

    public void RegistrarActividad(DateTime inicio)
    {
        if (Estado == EstadoRuta.Despachada)
        {
            Estado = EstadoRuta.EnCurso;
        }

        if (FechaInicio is null || inicio < FechaInicio)
        {
            FechaInicio = inicio;
        }
    }

    public void FinalizarSiTermino(IEnumerable<EstadoEnvio> estadosDeSusEnvios)
    {
        if (Estado == EstadoRuta.EnCurso
            && estadosDeSusEnvios.All(e => e != EstadoEnvio.EnTransito))
        {
            Estado = EstadoRuta.Finalizada;
            FechaFin = DateTime.UtcNow;
        }
    }
}