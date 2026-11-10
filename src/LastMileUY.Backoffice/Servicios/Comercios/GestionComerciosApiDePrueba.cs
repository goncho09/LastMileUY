
using LastMileUY.Contracts.Administracion;
using LastMileUY.Contracts.Comercios;

namespace LastMileUY.Backoffice.Servicios;

public sealed class GestionComerciosApiDePrueba : IGestionComerciosApi
{
    private readonly List<ComercioAdminDto> _comercios = [];
    private readonly Dictionary<int, List<VinculoOperadorComercioDto>> _vinculos = [];
    private int _siguienteId = 1;

    public Task<List<ComercioAdminDto>> ListarAsync() =>
        Task.FromResult(_comercios.ToList());

    public Task<ComercioAdminDto?> ObtenerAsync(int id) =>
        Task.FromResult(_comercios.FirstOrDefault(x => x.Id == id));

    public Task<ComercioAdminDto> CrearAsync(CrearComercioConUsuarioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Usuario);

        var nuevo = new ComercioAdminDto(
            _siguienteId++,
            request.Nombre,
            request.RazonSocial,
            request.RUT,
            request.Email,
            request.Telefono,
            true,
            DateTime.UtcNow
        );

        _comercios.Add(nuevo);
        return Task.FromResult(nuevo);
    }

    public Task EditarAsync(int id, GuardarComercioAdminRequest request)
    {
        var indice = _comercios.FindIndex(x => x.Id == id);

        if (indice < 0)
            throw new KeyNotFoundException("Comercio no encontrado.");

        _comercios[indice] = _comercios[indice] with
        {
            Nombre = request.Nombre,
            RazonSocial = request.RazonSocial,
            RUT = request.RUT,
            Email = request.Email,
            Telefono = request.Telefono
        };

        return Task.CompletedTask;
    }

    public Task<List<VinculoOperadorComercioDto>> ListarVinculosAsync(int id) =>
        Task.FromResult(_vinculos.TryGetValue(id, out var lista)
            ? lista.ToList()
            : new List<VinculoOperadorComercioDto>());

    public Task VincularAsync(int comercioId, int operadorId)
    {
        if (_comercios.All(x => x.Id != comercioId))
            throw new KeyNotFoundException("Comercio no encontrado.");

        if (!_vinculos.TryGetValue(comercioId, out var lista))
            _vinculos[comercioId] = lista = [];

        var indice = lista.FindIndex(x => x.OperadorId == operadorId);

        if (indice >= 0)
        {
            lista[indice] = lista[indice] with { Activo = true };
        }
        else
        {
            lista.Add(new VinculoOperadorComercioDto(
                operadorId,
                $"Operador {operadorId}",
                true,
                DateTime.UtcNow
            ));
        }

        return Task.CompletedTask;
    }

    public Task DesvincularAsync(int comercioId, int operadorId)
    {
        if (_vinculos.TryGetValue(comercioId, out var lista))
        {
            var indice = lista.FindIndex(x => x.OperadorId == operadorId);

            if (indice >= 0)
                lista[indice] = lista[indice] with { Activo = false };
        }

        return Task.CompletedTask;
    }
}
