using LastMileUY.Contracts.Administracion;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public sealed class GestionOperadoresApiDePrueba : IGestionOperadoresApi
{
    private readonly List<OperadorAdminDto> _operadores = [];
    private int _siguienteId = 1;

    public Task<List<OperadorAdminDto>> ListarAsync() =>
        Task.FromResult(_operadores.ToList());

    public Task<OperadorAdminDto?> ObtenerAsync(int id) =>
        Task.FromResult(_operadores.FirstOrDefault(x => x.Id == id));

    public Task<OperadorAdminDto> CrearAsync(
        CrearOperadorConUsuarioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Usuario);

        var nuevo = new OperadorAdminDto(
            _siguienteId++,
            request.Nombre,
            true,
            DateTime.UtcNow,
            request.ColorPrimario,
            request.LogoUrl
        );

        _operadores.Add(nuevo);

        return Task.FromResult(nuevo);
    }

    public Task EditarAsync(
        int id,
        GuardarOperadorAdminRequest request)
    {
        var indice = _operadores.FindIndex(x => x.Id == id);

        if (indice < 0)
            throw new KeyNotFoundException("Operador no encontrado.");

        _operadores[indice] = _operadores[indice] with
        {
            Nombre = request.Nombre,
            ColorPrimario = request.ColorPrimario,
            LogoUrl = request.LogoUrl
        };

        return Task.CompletedTask;
    }
}