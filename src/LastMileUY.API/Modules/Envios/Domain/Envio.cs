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
    // Sin caracteres que se confundan entre sí (0/O, 1/I/L)
    private const string CaracteresCodigo = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LargoCodigo = 12;

    public int Id { get; set; }

    public int OperadorId { get; set; }

    public int ComercioId { get; set; }

    public int DestinatarioId { get; set; }

    // Copia de la dirección al crear el envío: si el destinatario después cambia
    // su dirección, el envío conserva la original. Se carga con AsignarDireccionEntrega.
    public DireccionEntrega DireccionEntrega { get; private set; } = null!;

    // Código aleatorio para el enlace de seguimiento público; el Id nunca se expone
    public string CodigoSeguimiento { get; set; } = GenerarCodigoSeguimiento();

    // Identificador del envío en el sistema del comercio; evita duplicados al reimportar
    public string? ReferenciaExterna { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEntrega { get; set; }

    public decimal? CostoEnvio { get; set; }

    // Solo cambia mediante CambiarEstado, que valida la transición
    public EstadoEnvio Estado { get; private set; } = EstadoEnvio.Admitido;

    public ModalidadEnvio Modalidad { get; set; } = ModalidadEnvio.Estandar;

    // Por dónde entró (portal del comercio o API): el Backoffice lo muestra y permite filtrarlo
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

    // Control de concurrencia optimista (columna xmin de PostgreSQL)
    public uint Version { get; private set; }

    // Intentos de entrega que fallaron (necesita IntentosEntrega cargados)
    public int IntentosFallidos =>
        IntentosEntrega.Count(i => !i.Exitoso);

    // Estados a los que se puede pasar desde el actual: la tabla de transiciones,
    // recortada por la regla operativa del operador (si tiene una para la modalidad del envío).
    public IReadOnlyList<EstadoEnvio> EstadosSiguientes(ReglaOperativa? regla = null) =>
        TransicionesEnvio.DestinosDesde(Estado)
            .Where(destino => MotivoDeRechazo(destino, regla) is null)
            .ToList();

    // Cambia el estado si la transición es válida y lo deja registrado en el historial.
    // origen: desde dónde se hizo (Backoffice, Repartidor, Sistema...); responsableId: el usuario.
    // regla: la regla operativa vigente del operador; sin regla solo aplica la tabla de transiciones.
    // Para pasar a Entregado o NoEntregado hay que tener cargados los IntentosEntrega.
    // ocurrido y ubicacion: cuándo y dónde pasó, si no es ahora (la app del repartidor registra
    // sin conexión y sincroniza después; ver SincronizarEntregasService).
    // Devuelve el intento de entrega que registró, si el cambio fue a Entregado o NoEntregado.
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

        // Llegar a Entregado o NoEntregado es el resultado de un intento de entrega
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

        // Terminó la devolución: los bultos que no se entregaron volvieron al comercio.
        // Los entregados no cambian (en una devolución de un envío entregado ya están Entregado).
        // Para que se marquen hay que tener cargados los Bultos.
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

    // Después de una entrega parcial, qué pasa con los bultos que quedaron, según la política del operador
    // (la versión de las reglas de entrega parcial con la que salió la hoja de ruta):
    // - NuevaHojaDeRuta: el envío se reprograma y sus bultos pendientes pueden salir en otra ruta.
    //   Si la regla operativa ya no permite más intentos, manda ese límite: va a devolución.
    // - Devolucion: el envío pasa a devolución.
    // No hace nada si el envío no quedó entregado parcialmente.
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

    // null si se puede pasar a ese estado; si no, el motivo para mostrarle al usuario
    private string? MotivoDeRechazo(EstadoEnvio nuevoEstado, ReglaOperativa? regla)
    {
        if (!TransicionesEnvio.EsValida(Estado, nuevoEstado))
        {
            return $"No se puede pasar el envío de {Estado} a {nuevoEstado}.";
        }

        // Agotados los intentos que permite el operador, el envío ya no se reprograma: va a devolución
        if (nuevoEstado == EstadoEnvio.Reprogramado
            && regla is not null
            && IntentosFallidos >= regla.MaximoIntentosEntrega)
        {
            return $"El envío ya tuvo {IntentosFallidos} intentos fallidos y la regla \"{regla.Nombre}\" "
                + $"permite {regla.MaximoIntentosEntrega}: tiene que pasar a devolución.";
        }

        return null;
    }

    // Primer evento del historial: el envío quedó admitido
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

        // Solo participan los bultos que todavía requieren entrega.
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

        // Validar antes de modificar el estado.
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

        // Contamos también los bultos entregados anteriormente.
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

        // Actualizar únicamente los bultos incluidos en el intento.
        foreach (var resultado in resultados)
        {
            var bulto = bultosPorEntregar.Single(b => b.Id == resultado.BultoId);

            // Un bulto previamente no entregado vuelve a quedar pendiente
            // antes de registrar el nuevo resultado.
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