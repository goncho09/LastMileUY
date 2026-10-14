using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.Backoffice.Servicios;

// Datos de prueba en memoria, para trabajar el Backoffice sin depender de la API.
// Se activa con "Api:UsarDatosDePrueba": true en la configuración.
public class EnviosApiDePrueba : IEnviosApi
{
    // Copia, solo para los datos de prueba, de la tabla de transiciones de la API (TransicionesEnvio)
    private static readonly Dictionary<string, string[]> Transiciones = new()
    {
        ["Admitido"] = ["EnDeposito"],
        ["EnDeposito"] = ["AsignadoARuta", "Extraviado"],
        ["AsignadoARuta"] = ["EnDeposito", "EnTransito", "Extraviado"],
        ["EnTransito"] = ["Entregado", "NoEntregado", "EnDeposito", "Extraviado"],
        ["NoEntregado"] = ["Reprogramado", "EnDevolucion"],
        ["Reprogramado"] = ["AsignadoARuta"],
        ["Entregado"] = ["EnDevolucion"],
        ["EnDevolucion"] = ["Devuelto", "Extraviado"],
    };

    // Reglas de ejemplo para que se vea el recuadro de la regla operativa (el límite de intentos lo aplica la API)
    private static readonly ReglaAplicadaDto ReglaEstandar = new("Estándar", 2, 24, 48, true, false, false);
    private static readonly ReglaAplicadaDto ReglaUrgente = new("Urgente", 1, 4, 12, true, false, false);

    private readonly List<EnvioDetalleDto> envios = GenerarEnvios();

    // Se puede cambiar el estado, así que la lista se toca con lock (el servicio es único para todos los pedidos)
    private readonly Lock bloqueo = new();

    public Task<PaginaDto<EnvioResumenDto>> ListarAsync(FiltroEnvios filtro, CancellationToken ct = default)
    {
        IEnumerable<EnvioDetalleDto> consulta;

        lock (bloqueo)
        {
            consulta = envios.ToList();
        }

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            consulta = consulta.Where(e => e.Estado == filtro.Estado);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Canal))
        {
            consulta = consulta.Where(e => e.Canal == filtro.Canal);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var texto = filtro.Buscar.Trim();
            consulta = consulta.Where(e =>
                Contiene(e.CodigoSeguimiento, texto) ||
                Contiene(e.ReferenciaExterna, texto) ||
                Contiene($"{e.Destinatario.Nombre} {e.Destinatario.Apellido}", texto));
        }

        var filtrados = consulta.OrderByDescending(e => e.FechaCreacion).ToList();
        var pagina = Math.Max(1, filtro.Pagina);

        var items = filtrados
            .Skip((pagina - 1) * filtro.TamanioPagina)
            .Take(filtro.TamanioPagina)
            .Select(AResumen)
            .ToList();

        return Task.FromResult(new PaginaDto<EnvioResumenDto>(items, filtrados.Count, pagina, filtro.TamanioPagina));
    }

    public Task<EnvioDetalleDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            return Task.FromResult(envios.FirstOrDefault(e => e.Id == id));
        }
    }

    public Task<EnvioDetalleDto?> CambiarEstadoAsync(int id, string estado, string? descripcion, CancellationToken ct = default)
    {
        lock (bloqueo)
        {
            var indice = envios.FindIndex(e => e.Id == id);

            if (indice < 0)
            {
                return Task.FromResult<EnvioDetalleDto?>(null);
            }

            var actual = envios[indice];

            if (!actual.EstadosSiguientes.Contains(estado))
            {
                throw new OperacionRechazadaException($"No se puede pasar el envío de {actual.Estado} a {estado}.");
            }

            var evento = new EventoEnvioDto(
                DateTime.UtcNow,
                estado,
                string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
                "Backoffice");

            var nuevo = actual with
            {
                Estado = estado,
                FechaEntrega = estado == "Entregado" ? DateTime.UtcNow : actual.FechaEntrega,
                Eventos = [.. actual.Eventos, evento],
                EstadosSiguientes = Siguientes(estado)
            };

            envios[indice] = nuevo;

            return Task.FromResult<EnvioDetalleDto?>(nuevo);
        }
    }

    private static string[] Siguientes(string estado) =>
        Transiciones.TryGetValue(estado, out var destinos) ? destinos : [];

    private static bool Contiene(string? valor, string texto) =>
        valor is not null && valor.Contains(texto, StringComparison.OrdinalIgnoreCase);

    private static EnvioResumenDto AResumen(EnvioDetalleDto e) => new(
        e.Id,
        e.CodigoSeguimiento,
        e.ReferenciaExterna,
        e.Comercio,
        $"{e.Destinatario.Nombre} {e.Destinatario.Apellido}",
        e.Direccion.Ciudad,
        e.Direccion.Departamento,
        e.Estado,
        e.Modalidad,
        e.Bultos.Count,
        e.FechaCreacion)
    {
        Canal = e.Canal
    };

    // Siempre genera los mismos datos (semilla fija), así las pruebas y las capturas no cambian
    private static List<EnvioDetalleDto> GenerarEnvios()
    {
        var azar = new Random(17);
        string[] comercios = ["Tienda Norte", "Librería Central", "Ferretería del Sur"];
        string[] nombres = ["Ana", "Bruno", "Carla", "Diego", "Elena", "Federico", "Gabriela", "Hugo"];
        string[] apellidos = ["Pereira", "Rodríguez", "Silva", "Fernández", "Martínez", "Sosa"];
        string[] calles = ["Av. 18 de Julio", "Bulevar Artigas", "Rivera", "Colonia", "Av. Italia"];
        (string Ciudad, string Departamento)[] lugares =
        [
            ("Montevideo", "Montevideo"), ("Ciudad de la Costa", "Canelones"),
            ("Las Piedras", "Canelones"), ("Maldonado", "Maldonado"), ("San José de Mayo", "San José")
        ];
        var estados = EstadosEnvio.Todos.Select(e => e.Valor).ToArray();
        var inicio = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        var lista = new List<EnvioDetalleDto>();

        for (var id = 1; id <= 45; id++)
        {
            var lugar = lugares[azar.Next(lugares.Length)];
            var estado = estados[azar.Next(estados.Length)];
            var creado = inicio.AddHours(id * 5);
            var regla = id % 5 == 0 ? ReglaUrgente : ReglaEstandar;

            var bultos = Enumerable.Range(1, azar.Next(1, 4))
                .Select(n => new BultoDto(
                    $"B{id:000}-{n}",
                    Math.Round((decimal)(azar.NextDouble() * 9 + 0.5), 2),
                    azar.Next(10, 60),
                    azar.Next(10, 40),
                    azar.Next(5, 30),
                    n == 1 ? "Caja" : null))
                .ToList();

            lista.Add(new EnvioDetalleDto(
                Id: id,
                CodigoSeguimiento: $"PRB{id:000}{(char)('A' + id % 26)}XK7M2Q",
                ReferenciaExterna: id % 4 == 0 ? null : $"PED-{1000 + id}",
                Comercio: comercios[id % comercios.Length],
                Estado: estado,
                Modalidad: id % 5 == 0 ? "Urgente" : "Estandar",
                FechaCreacion: creado,
                FechaEntrega: estado == "Entregado" ? creado.AddDays(1) : null,
                CostoEnvio: 150 + id * 3,
                Destinatario: new DestinatarioDto(
                    nombres[azar.Next(nombres.Length)],
                    apellidos[azar.Next(apellidos.Length)],
                    $"09{azar.Next(1000000, 9999999)}",
                    id % 3 == 0 ? null : $"destinatario{id}@correo.test"),
                Direccion: new DireccionDto(
                    calles[azar.Next(calles.Length)],
                    azar.Next(100, 3999).ToString(),
                    id % 2 == 0 ? $"Apto {azar.Next(101, 999)}" : null,
                    lugar.Ciudad,
                    lugar.Departamento,
                    null,
                    id % 3 == 0 ? "Portón verde" : null),
                Bultos: bultos,
                Eventos: GenerarEventos(estado, creado))
            {
                EstadosSiguientes = Siguientes(estado),
                Regla = regla,
                FechaLimiteEntrega = creado.AddHours(regla.PlazoEntregaHoras),
                // Uno de cada tres entró por el portal del comercio
                Canal = id % 3 == 0 ? "Portal" : "Api"
            });
        }

        return lista;
    }

    // Arma un historial coherente: los estados por los que pasó hasta llegar al actual
    private static List<EventoEnvioDto> GenerarEventos(string estadoFinal, DateTime creado)
    {
        string[] camino = estadoFinal switch
        {
            "Admitido" => ["Admitido"],
            "EnDeposito" => ["Admitido", "EnDeposito"],
            "AsignadoARuta" => ["Admitido", "EnDeposito", "AsignadoARuta"],
            "EnTransito" => ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito"],
            "Entregado" => ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito", "Entregado"],
            "NoEntregado" => ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado"],
            "Reprogramado" => ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado", "Reprogramado"],
            "EnDevolucion" => ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado", "EnDevolucion"],
            "Devuelto" => ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado", "EnDevolucion", "Devuelto"],
            "Extraviado" => ["Admitido", "EnDeposito", "Extraviado"],
            _ => ["Admitido"]
        };

        return camino
            .Select((estado, i) => new EventoEnvioDto(
                creado.AddHours(i * 6),
                estado,
                $"Pasó a: {EstadosEnvio.Texto(estado)}",
                estado is "EnTransito" or "Entregado" or "NoEntregado" ? "Repartidor" : "Backoffice"))
            .ToList();
    }
}
