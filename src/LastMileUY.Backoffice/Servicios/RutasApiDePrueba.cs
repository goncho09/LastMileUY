using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

// Hojas de ruta de prueba en memoria, para trabajar el Backoffice sin la API ("Api:UsarDatosDePrueba").
// Repite las reglas principales de la API (capacidad, franjas en orden, un repartidor y un vehículo
// por día, un envío en una sola ruta) para que la pantalla muestre los mismos rechazos.
public class RutasApiDePrueba : IRutasApi
{
    private static readonly string[] EstadosAbiertos = ["Planificada", "Despachada", "EnCurso"];

    private static readonly FranjaHorariaDto Manana = new("Mañana", new TimeOnly(9, 0), new TimeOnly(12, 0));
    private static readonly FranjaHorariaDto Tarde = new("Tarde", new TimeOnly(14, 0), new TimeOnly(18, 0));

    private readonly List<RepartidorDto> repartidores =
    [
        new(1, "Lucía Méndez"),
        new(2, "Martín Suárez")
    ];

    private readonly List<VehiculoDto> vehiculos =
    [
        new(1, "MOT 1000", "Moto", 30, 0.15m),
        new(2, "FUR 2000", "Furgoneta", 800, 6)
    ];

    // Envíos en depósito que todavía no están en ninguna ruta
    private readonly List<EnvioDisponibleDto> disponibles =
    [
        new(201, "K7M2Q9T4XW3P", "EnDeposito", "Ana Silva", "Rivera 1234, Montevideo", Manana, 2.5m, 0.012m),
        new(202, "R4T8Y2U6P9HZ", "EnDeposito", "Bruno Pereira", "Ejido 55, Montevideo", null, 12, 0.08m),
        new(203, "M3N7B5V9C2XQ", "Reprogramado", "Carla Rodríguez", "Colonia 900, Montevideo", Tarde, 1, 0.004m),
        new(204, "Z9X5C3V7B2NM", "EnDeposito", "Diego Fernández", "18 de Julio 1500, Montevideo", Manana, 20, 0.11m),
        new(205, "Q2W6E4R8T3YU", "EnDeposito", "Elena Gómez", "Bulevar Artigas 800, Montevideo", Tarde, 0.8m, 0.002m)
    ];

    private readonly List<RutaDetalleDto> rutas = [];

    private readonly Lock bloqueo = new();

    private int ultimoId;

    public Task<PaginaDto<RutaResumenDto>> ListarAsync(FiltroRutas filtro, CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            var filtradas = rutas
                .Where(r => filtro.Fecha is null || r.Fecha == filtro.Fecha)
                .Where(r => string.IsNullOrWhiteSpace(filtro.Estado) || r.Estado == filtro.Estado)
                .OrderByDescending(r => r.Fecha)
                .ThenByDescending(r => r.Id)
                .ToList();

            var items = filtradas
                .Skip((filtro.Pagina - 1) * filtro.TamanioPagina)
                .Take(filtro.TamanioPagina)
                .Select(r => new RutaResumenDto(
                    r.Id, r.Nombre, r.Fecha, r.Estado, r.Repartidor.Nombre, r.Vehiculo.Matricula, r.Paradas.Count))
                .ToList();

            return Task.FromResult(new PaginaDto<RutaResumenDto>(items, filtradas.Count, filtro.Pagina, filtro.TamanioPagina));
        }
    }

    public Task<RutaDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            return Task.FromResult(rutas.FirstOrDefault(r => r.Id == id));
        }
    }

    public Task<OpcionesRutaDto> ObtenerOpcionesAsync(CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            return Task.FromResult(new OpcionesRutaDto(repartidores.ToList(), vehiculos.ToList(), disponibles.ToList()));
        }
    }

    public Task<RutaDetalleDto> CrearAsync(CrearRutaRequest ruta, CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            if (string.IsNullOrWhiteSpace(ruta.Nombre))
                throw new OperacionRechazadaException("La ruta necesita un nombre.");

            if (ruta.Fecha < DateOnly.FromDateTime(DateTime.Today))
                throw new OperacionRechazadaException("La fecha de la ruta no puede ser anterior a hoy.");

            if (ruta.EnvioIds.Count == 0)
                throw new OperacionRechazadaException("La ruta tiene que tener al menos un envío.");

            var repartidor = repartidores.FirstOrDefault(r => r.Id == ruta.RepartidorId)
                ?? throw new OperacionRechazadaException("El repartidor no existe.");
            var vehiculo = vehiculos.FirstOrDefault(v => v.Id == ruta.VehiculoId)
                ?? throw new OperacionRechazadaException("El vehículo no existe.");

            var delDia = rutas.Where(r => r.Fecha == ruta.Fecha && EstadosAbiertos.Contains(r.Estado)).ToList();

            if (delDia.Any(r => r.Repartidor.Id == repartidor.Id))
                throw new OperacionRechazadaException($"El repartidor ya tiene una ruta para el {ruta.Fecha:dd/MM/yyyy}.");

            if (delDia.Any(r => r.Vehiculo.Id == vehiculo.Id))
                throw new OperacionRechazadaException(
                    $"El vehículo {vehiculo.Matricula} ya está en una ruta el {ruta.Fecha:dd/MM/yyyy}.");

            var envios = new List<EnvioDisponibleDto>();

            foreach (var id in ruta.EnvioIds)
            {
                var envio = disponibles.FirstOrDefault(e => e.Id == id)
                    ?? throw new OperacionRechazadaException($"El envío {id} no existe o ya está en otra ruta.");

                if (envios.Contains(envio))
                    throw new OperacionRechazadaException($"El envío {id} está más de una vez en la ruta.");

                envios.Add(envio);
            }

            var peso = envios.Sum(e => e.PesoKg);
            var volumen = envios.Sum(e => e.VolumenM3);

            if (peso > vehiculo.CapacidadPesoKg)
                throw new OperacionRechazadaException(
                    $"Los envíos pesan {peso:0.##} kg y el vehículo {vehiculo.Matricula} carga hasta {vehiculo.CapacidadPesoKg:0.##} kg.");

            if (volumen > vehiculo.CapacidadVolumenM3)
                throw new OperacionRechazadaException(
                    $"Los envíos ocupan {volumen:0.###} m³ y el vehículo {vehiculo.Matricula} tiene {vehiculo.CapacidadVolumenM3:0.###} m³.");

            var conFranja = envios.Where(e => e.Franja is not null).ToList();

            for (var i = 1; i < conFranja.Count; i++)
            {
                if (conFranja[i].Franja!.HoraInicio < conFranja[i - 1].Franja!.HoraInicio)
                    throw new OperacionRechazadaException(
                        $"El envío {conFranja[i].CodigoSeguimiento} (franja \"{conFranja[i].Franja!.Nombre}\") no puede ir "
                        + $"después de una parada de la franja \"{conFranja[i - 1].Franja!.Nombre}\": "
                        + "las paradas con franja van en orden de horario.");
            }

            var paradas = envios
                .Select((e, i) => new ParadaDto(
                    i + 1, e.Id, e.CodigoSeguimiento, "AsignadoARuta",
                    Destinatario(e), Direccion(e), e.Franja, 1, e.PesoKg, e.VolumenM3))
                .ToList();

            var creada = new RutaDetalleDto(
                ++ultimoId, ruta.Nombre.Trim(), ruta.Fecha, "Planificada", repartidor, vehiculo, peso, volumen, paradas);

            rutas.Add(creada);
            disponibles.RemoveAll(envios.Contains);

            return Task.FromResult(creada);
        }
    }

    public Task<RutaDetalleDto?> DespacharAsync(int id, CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            var indice = rutas.FindIndex(r => r.Id == id);

            if (indice < 0)
                return Task.FromResult<RutaDetalleDto?>(null);

            var ruta = rutas[indice];

            if (ruta.Estado != "Planificada")
                throw new OperacionRechazadaException(
                    $"Solo se puede despachar una ruta planificada (esta está {ruta.Estado}).");

            var despachada = ruta with
            {
                Estado = "Despachada",
                Paradas = ruta.Paradas.Select(p => p with { EstadoEnvio = "EnTransito" }).ToList()
            };

            rutas[indice] = despachada;

            return Task.FromResult<RutaDetalleDto?>(despachada);
        }
    }

    // Los datos de prueba solo tienen el nombre completo y la dirección en una línea
    private static DestinatarioDto Destinatario(EnvioDisponibleDto envio)
    {
        var partes = envio.Destinatario.Split(' ', 2);
        return new DestinatarioDto(partes[0], partes.ElementAtOrDefault(1) ?? "", null, null);
    }

    private static DireccionDto Direccion(EnvioDisponibleDto envio)
    {
        var calleYCiudad = envio.Direccion.Split(", ", 2);
        var calle = calleYCiudad[0];
        var espacio = calle.LastIndexOf(' ');

        return new DireccionDto(
            espacio < 0 ? calle : calle[..espacio],
            espacio < 0 ? "" : calle[(espacio + 1)..],
            null,
            calleYCiudad.ElementAtOrDefault(1) ?? "",
            "Montevideo",
            null,
            null);
    }
}
