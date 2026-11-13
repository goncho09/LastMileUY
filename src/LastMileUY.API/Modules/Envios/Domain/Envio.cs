using System.Security.Cryptography;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;

namespace LastMileUY.API.Modules.Envios.Domain;

public class Envio : IPerteneceAOperador, IPerteneceAComercio
{
    private const string CaracteresCodigo = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LargoCodigo = 12;

    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int DestinatarioId { get; set; }

    public DireccionEntrega DireccionEntrega { get; private set; } = null!;

    public string CodigoSeguimiento { get; set; } = GenerarCodigoSeguimiento();

    public string? ReferenciaExterna { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEntrega { get; set; }

    public decimal? CostoEnvio { get; set; }

    public EstadoEnvio Estado { get; private set; } = EstadoEnvio.Admitido;

    public ModalidadEnvio Modalidad { get; set; } = ModalidadEnvio.Estandar;

    public CanalIngreso Canal { get; set; } = CanalIngreso.Api;

    public int? FranjaHorariaId { get; set; }

    public Devolucion? Devolucion { get; set; }

    public int? CuadroTarifarioId { get; set; }

    public ICollection<Bulto> Bultos { get; set; } = new List<Bulto>();

    public ICollection<EventoEnvio> Eventos { get; set; }
        = new List<EventoEnvio>();

    public ICollection<IntentoEntrega> IntentosEntrega { get; set; }
        = new List<IntentoEntrega>();

    public ICollection<Parada> Paradas { get; set; }
        = new List<Parada>();

    public ICollection<Notificacion> Notificaciones { get; set; }
        = new List<Notificacion>();

    public ICollection<EntregaAviso> EntregasAvisos { get; set; }
        = new List<EntregaAviso>();

    public ICollection<DetalleLiquidacion> DetallesLiquidacion { get; set; }
        = new List<DetalleLiquidacion>();

    public uint Version { get; private set; }

    public int IntentosFallidos =>
        IntentosEntrega.Count(i => !i.Exitoso);

    public IReadOnlyList<EstadoEnvio> EstadosSiguientes(ReglaOperativa? regla = null) =>
        TransicionesEnvio.DestinosDesde(Estado)
            .Where(destino => MotivoDeRechazo(destino, regla) is null)
            .ToList();

    public IntentoEntrega? CambiarEstado(
        EstadoEnvio nuevoEstado,
        string origen = "Sistema",
        string? descripcion = null,
        string? responsableId = null,
        ReglaOperativa? regla = null,
        DateTime? ocurrido = null,
        (decimal Latitud, decimal Longitud)? ubicacion = null)
    {
        var motivo = MotivoDeRechazo(nuevoEstado, regla);

        if (motivo is not null)
        {
            throw new InvalidOperationException(motivo);
        }

        var cuando = ocurrido ?? DateTime.UtcNow;
        IntentoEntrega? intento = null;

        if (nuevoEstado is EstadoEnvio.Entregado or EstadoEnvio.NoEntregado or EstadoEnvio.EntregadoParcialmente)
        {
            intento = new IntentoEntrega
            {
                NumeroIntento = IntentosEntrega.Count + 1,
                FechaHora = cuando,
                Exitoso = nuevoEstado == EstadoEnvio.Entregado,
                Parcial = nuevoEstado == EstadoEnvio.EntregadoParcialmente,
                Observaciones = descripcion
            };

            IntentosEntrega.Add(intento);
        }

        Estado = nuevoEstado;

        if (nuevoEstado == EstadoEnvio.Entregado)
        {
            FechaEntrega = cuando;
        }

        if (nuevoEstado == EstadoEnvio.Devuelto)
        {
            foreach (var bulto in Bultos.Where(b => b.Estado is EstadoBulto.Pendiente or EstadoBulto.NoEntregado))
            {
                bulto.CambiarEstado(EstadoBulto.Devuelto);
            }
        }

        RegistrarEvento(origen, descripcion, responsableId, cuando, ubicacion);

        return intento;
    }

    public void AplicarPoliticaBultosPendientes(
        ReglaEntregaParcial reglaEntregaParcial,
        ReglaOperativa? regla = null,
        string? responsableId = null,
        DateTime? ocurrido = null)
    {
        ArgumentNullException.ThrowIfNull(reglaEntregaParcial);

        if (Estado != EstadoEnvio.EntregadoParcialmente)
        {
            return;
        }

        var version = $"reglas de entrega parcial v{reglaEntregaParcial.Version}";

        if (reglaEntregaParcial.DestinoBultosPendientes == DestinoBultosPendientes.NuevaHojaDeRuta)
        {
            if (MotivoDeRechazo(EstadoEnvio.Reprogramado, regla) is null)
            {
                var recargo = reglaEntregaParcial.RecargoNuevaVisita;

                CambiarEstado(
                    EstadoEnvio.Reprogramado,
                    "Sistema",
                    $"Bultos pendientes: vuelven a salir en otra hoja de ruta ({version}). "
                    + (recargo > 0
                        ? $"Recargo por nueva visita: ${recargo:0.00}."
                        : "Sin recargo por nueva visita."),
                    responsableId,
                    regla,
                    ocurrido);

                if (recargo > 0)
                {
                    CostoEnvio = (CostoEnvio ?? 0m) + recargo;
                }

                return;
            }

            CambiarEstado(EstadoEnvio.EnDevolucion, "Sistema",
                $"Bultos pendientes: iban a salir en otra hoja de ruta ({version}), pero se agotaron los "
                + $"intentos de la regla \"{regla?.Nombre}\". Vuelven al comercio.",
                responsableId, regla, ocurrido);
            return;
        }

        CambiarEstado(EstadoEnvio.EnDevolucion, "Sistema",
            $"Bultos pendientes: vuelven al comercio ({version}).",
            responsableId, regla, ocurrido);
    }

    private string? MotivoDeRechazo(EstadoEnvio nuevoEstado, ReglaOperativa? regla)
    {
        if (!TransicionesEnvio.EsValida(Estado, nuevoEstado))
        {
            return $"No se puede pasar el envío de {Estado} a {nuevoEstado}.";
        }

        if (nuevoEstado == EstadoEnvio.Reprogramado
            && regla is not null
            && IntentosFallidos >= regla.MaximoIntentosEntrega)
        {
            return $"El envío ya tuvo {IntentosFallidos} intentos fallidos y la regla \"{regla.Nombre}\" "
                + $"permite {regla.MaximoIntentosEntrega}: tiene que pasar a devolución.";
        }

        return null;
    }

    public void RegistrarAdmision(string origen, string? responsableId = null) =>
        RegistrarEvento(origen, "Envío admitido", responsableId, DateTime.UtcNow, null);

    private void RegistrarEvento(
        string origen,
        string? descripcion,
        string? responsableId,
        DateTime cuando,
        (decimal Latitud, decimal Longitud)? ubicacion) =>
        Eventos.Add(new EventoEnvio
        {
            Tipo = Estado.ToString(),
            Descripcion = descripcion,
            Origen = origen,
            ResponsableId = responsableId,
            FechaHora = cuando,
            Latitud = ubicacion?.Latitud,
            Longitud = ubicacion?.Longitud
        });

    public EstadoEnvio? CalcularEstadoEntregaPorBultos()
    {
        if (Bultos.Count == 0)
            return null;

        var entregados = Bultos.Count(
            b => b.Estado == EstadoBulto.Entregado);

        if (entregados == Bultos.Count)
            return EstadoEnvio.Entregado;

        if (entregados > 0)
            return EstadoEnvio.EntregadoParcialmente;

        return null;
    }

    public IntentoEntrega RegistrarResultadoBultos(
    IReadOnlyCollection<ResultadoBultoIntento> resultados,
    string origen = "Repartidor",
    string? responsableId = null,
    ReglaOperativa? regla = null,
    DateTime? ocurrido = null,
    (decimal Latitud, decimal Longitud)? ubicacion = null)
    {
        ArgumentNullException.ThrowIfNull(resultados);

        if (Estado != EstadoEnvio.EnTransito)
            throw new InvalidOperationException(
                "El envío debe estar en tránsito para registrar resultados de bultos.");

        if (Bultos.Count == 0)
            throw new InvalidOperationException("El envío no tiene bultos.");

        var bultosPorEntregar = Bultos
            .Where(b => b.Estado is EstadoBulto.Pendiente or EstadoBulto.NoEntregado)
            .ToList();

        if (bultosPorEntregar.Count == 0)
            throw new InvalidOperationException(
                "El envío no tiene bultos pendientes de entrega.");

        if (resultados.Count != bultosPorEntregar.Count)
            throw new InvalidOperationException(
                "Se debe informar el resultado de todos los bultos pendientes de entrega.");

        var ids = resultados.Select(r => r.BultoId).ToList();

        if (ids.Distinct().Count() != ids.Count ||
            ids.Any(id => bultosPorEntregar.All(b => b.Id != id)))
        {
            throw new InvalidOperationException(
                "Hay bultos duplicados o que no corresponden a esta entrega.");
        }

        foreach (var resultado in resultados)
        {
            if (resultado.Entregado && resultado.MotivoNoEntregaId is not null)
                throw new InvalidOperationException(
                    "Un bulto entregado no puede tener motivo de no entrega.");

            if (!resultado.Entregado && resultado.MotivoNoEntregaId is null)
                throw new InvalidOperationException(
                    "Los bultos no entregados deben tener un motivo.");
        }

        var entregadosEnEsteIntento = resultados.Count(r => r.Entregado);

        var entregadosAnteriormente = Bultos.Count(
            b => b.Estado == EstadoBulto.Entregado);

        var totalEntregados = entregadosAnteriormente + entregadosEnEsteIntento;

        var nuevoEstado = totalEntregados switch
        {
            0 => EstadoEnvio.NoEntregado,
            _ when totalEntregados == Bultos.Count => EstadoEnvio.Entregado,
            _ => EstadoEnvio.EntregadoParcialmente
        };

        var motivo = MotivoDeRechazo(nuevoEstado, regla);

        if (motivo is not null)
            throw new InvalidOperationException(motivo);

        foreach (var resultado in resultados)
        {
            var bulto = bultosPorEntregar.Single(b => b.Id == resultado.BultoId);

            if (bulto.Estado == EstadoBulto.NoEntregado)
                bulto.CambiarEstado(EstadoBulto.Pendiente);

            bulto.CambiarEstado(
                resultado.Entregado
                    ? EstadoBulto.Entregado
                    : EstadoBulto.NoEntregado,
                resultado.MotivoNoEntregaId);
        }

        var intento = CambiarEstado(
            nuevoEstado,
            origen,
            $"Resultado por bultos: {entregadosEnEsteIntento} de {bultosPorEntregar.Count} entregados en este intento. " +
            $"Total entregados: {totalEntregados} de {Bultos.Count}.",
            responsableId,
            regla,
            ocurrido,
            ubicacion)!;

        foreach (var resultado in resultados)
            intento.ResultadosBultos.Add(resultado);

        return intento;
    }

    public void AsignarDireccionEntrega(DireccionEntrega direccion)
    {
        ArgumentNullException.ThrowIfNull(direccion);

        DireccionEntrega = direccion;
    }

    public static string GenerarCodigoSeguimiento() =>
        RandomNumberGenerator.GetString(CaracteresCodigo, LargoCodigo);
}