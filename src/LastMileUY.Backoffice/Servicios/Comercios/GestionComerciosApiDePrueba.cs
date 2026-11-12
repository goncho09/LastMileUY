using LastMileUY.Contracts.Administracion;
using LastMileUY.Contracts.Comercios;

namespace LastMileUY.Backoffice.Servicios;

// Administración global de comercios con los datos de prueba (Api:UsarDatosDePrueba = true).
// Singleton con candado, como GestionOperadoresApiDePrueba, y con los operadores de esa misma versión
// de prueba: el nombre de cada vínculo es el del operador, y no se puede vincular uno que no existe.
public sealed class GestionComerciosApiDePrueba(GestionOperadoresApiDePrueba operadores) : IGestionComerciosApi
{
    private readonly Lock candado = new();
    private readonly List<ComercioAdminDto> comercios =
    [
        new(1, "Tienda del Centro", "Tienda del Centro S.A.", "210123450019", "ventas@tiendacentro.test",
            "2900 1234", true, DateTime.UtcNow.AddMonths(-2))
    ];

    private readonly Dictionary<int, List<VinculoOperadorComercioDto>> vinculos = new()
    {
        [1] = [new(OperadorApiDePrueba.Operador.Id, OperadorApiDePrueba.Operador.Nombre, true, DateTime.UtcNow.AddMonths(-2))]
    };

    public Task<List<ComercioAdminDto>> ListarAsync()
    {
        lock (candado)
            return Task.FromResult(comercios.OrderBy(c => c.Nombre).ToList());
    }

    public Task<ComercioAdminDto?> ObtenerAsync(int id)
    {
        lock (candado)
            return Task.FromResult(comercios.FirstOrDefault(x => x.Id == id));
    }

    public Task<ComercioAdminDto> CrearAsync(CrearComercioConUsuarioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Usuario);

        lock (candado)
        {
            if (!string.IsNullOrWhiteSpace(request.RUT) && comercios.Any(c => c.RUT == request.RUT.Trim()))
                throw new OperacionRechazadaException("Ya existe un comercio con ese RUT.");

            operadores.ReservarCorreo(request.Usuario.Email);

            var nuevo = new ComercioAdminDto(
                comercios.Count == 0 ? 1 : comercios.Max(c => c.Id) + 1,
                request.Nombre.Trim(),
                request.RazonSocial,
                request.RUT?.Trim(),
                request.Email,
                request.Telefono,
                true,
                DateTime.UtcNow);

            comercios.Add(nuevo);
            return Task.FromResult(nuevo);
        }
    }

    public Task EditarAsync(int id, GuardarComercioAdminRequest request)
    {
        lock (candado)
        {
            var indice = comercios.FindIndex(x => x.Id == id);

            if (indice < 0)
                throw new OperacionRechazadaException("El comercio no existe.");

            comercios[indice] = comercios[indice] with
            {
                Nombre = request.Nombre.Trim(),
                RazonSocial = request.RazonSocial,
                RUT = request.RUT?.Trim(),
                Email = request.Email,
                Telefono = request.Telefono
            };

            return Task.CompletedTask;
        }
    }

    public Task<List<VinculoOperadorComercioDto>> ListarVinculosAsync(int id)
    {
        lock (candado)
            return Task.FromResult(vinculos.TryGetValue(id, out var lista) ? lista.ToList() : []);
    }

    public Task VincularAsync(int comercioId, int operadorId)
    {
        lock (candado)
        {
            if (comercios.All(x => x.Id != comercioId))
                throw new OperacionRechazadaException("El comercio no existe.");

            var nombre = operadores.NombreDe(operadorId)
                         ?? throw new OperacionRechazadaException("El operador no existe.");

            if (!vinculos.TryGetValue(comercioId, out var lista))
                vinculos[comercioId] = lista = [];

            var indice = lista.FindIndex(x => x.OperadorId == operadorId);

            if (indice >= 0)
                lista[indice] = lista[indice] with { Activo = true };
            else
                lista.Add(new VinculoOperadorComercioDto(operadorId, nombre, true, DateTime.UtcNow));

            return Task.CompletedTask;
        }
    }

    public Task DesvincularAsync(int comercioId, int operadorId)
    {
        lock (candado)
        {
            if (vinculos.TryGetValue(comercioId, out var lista)
                && lista.FindIndex(x => x.OperadorId == operadorId) is >= 0 and var indice)
            {
                lista[indice] = lista[indice] with { Activo = false };
            }

            return Task.CompletedTask;
        }
    }
}
