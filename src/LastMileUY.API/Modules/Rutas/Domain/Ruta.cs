namespace LastMileUY.API.Modules.Rutas.Domain;

using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

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

    // Versión de las reglas de entrega parcial con la que salió la ruta: se fija al despachar y no cambia,
    // aunque el operador publique otra mientras el repartidor está en la calle. null: el operador no tenía
    // ninguna publicada (se permite la entrega parcial, como antes de que existieran las versiones).
    public int? ReglaEntregaParcialId { get; set; }

    public ReglaEntregaParcial? ReglaEntregaParcial { get; set; }

    public Rendicion? Rendicion { get; set; }
    
    public ICollection<Parada> Paradas { get; set; }
        = new List<Parada>();
    
    public ICollection<Posicion> Posiciones { get; set; }
        = new List<Posicion>();
    
    public ICollection<Incidencia> Incidencias { get; set; }
        = new List<Incidencia>();

    // Estados en los que la ruta todavía tiene a cargo a su repartidor, su vehículo y sus envíos
    public static readonly EstadoRuta[] EstadosAbiertos =
        [EstadoRuta.Planificada, EstadoRuta.Despachada, EstadoRuta.EnCurso];

    // Estados en los que el repartidor asignado puede descargar la hoja de ruta
    public static readonly EstadoRuta[] EstadosParaElRepartidor =
        [EstadoRuta.Despachada, EstadoRuta.EnCurso];

    // Despachar: la ruta queda lista para que el repartidor la descargue y salga.
    // Los envíos se pasan a En tránsito desde el servicio (ver PlanificarRutaService).
    // reglaEntregaParcial: la versión vigente del operador; queda fija para toda la ruta.
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

    // La primera entrega o intento que sincroniza el repartidor pone la ruta en curso.
    // inicio: cuándo pasó en el dispositivo (puede llegar más tarde, al recuperar conexión).
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

    // Cuando ningún envío de la ruta sigue en tránsito, el recorrido terminó
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