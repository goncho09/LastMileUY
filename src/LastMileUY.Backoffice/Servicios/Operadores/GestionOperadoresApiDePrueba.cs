using LastMileUY.Contracts.Administracion;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// Administración global de operadores con los datos de prueba (Api:UsarDatosDePrueba = true).
// Singleton, como los demás servicios de prueba: los datos tienen que durar entre un pedido y otro
// (si no, lo que se crea desaparece al volver al listado). Por eso el candado: lo usan varios pedidos a la vez.
// Arranca con el operador de prueba del resto del Backoffice y uno más.
public sealed class GestionOperadoresApiDePrueba : IGestionOperadoresApi
{
    private readonly Lock candado = new();
    private readonly List<OperadorAdminDto> operadores =
    [
        new(OperadorApiDePrueba.Operador.Id, OperadorApiDePrueba.Operador.Nombre, true,
            DateTime.UtcNow.AddMonths(-3), OperadorApiDePrueba.Operador.ColorPrimario, null),
        new(2, "Envíos del Sur", true, DateTime.UtcNow.AddMonths(-1), "#0EA5E9", null)
    ];

    // Correos de los usuarios iniciales (de operadores y comercios): la API no deja repetirlos
    private readonly HashSet<string> correos = new(StringComparer.OrdinalIgnoreCase);

    public Task<List<OperadorAdminDto>> ListarAsync()
    {
        lock (candado)
            return Task.FromResult(operadores.OrderBy(o => o.Nombre).ToList());
    }

    public Task<OperadorAdminDto?> ObtenerAsync(int id)
    {
        lock (candado)
            return Task.FromResult(operadores.FirstOrDefault(x => x.Id == id));
    }

    public Task<OperadorAdminDto> CrearAsync(CrearOperadorConUsuarioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Usuario);

        lock (candado)
        {
            ReservarCorreo(request.Usuario.Email);

            var nuevo = new OperadorAdminDto(
                operadores.Max(o => o.Id) + 1,
                request.Nombre.Trim(),
                true,
                DateTime.UtcNow,
                request.ColorPrimario,
                request.LogoUrl);

            operadores.Add(nuevo);
            return Task.FromResult(nuevo);
        }
    }

    public Task EditarAsync(int id, GuardarOperadorAdminRequest request)
    {
        lock (candado)
        {
            var indice = operadores.FindIndex(x => x.Id == id);

            if (indice < 0)
                throw new OperacionRechazadaException("El operador no existe.");

            operadores[indice] = operadores[indice] with
            {
                Nombre = request.Nombre.Trim(),
                ColorPrimario = request.ColorPrimario,
                LogoUrl = request.LogoUrl
            };

            return Task.CompletedTask;
        }
    }

    // Lo usa también la administración de comercios de prueba: los correos son únicos entre todos
    public void ReservarCorreo(string email)
    {
        lock (candado)
        {
            if (!correos.Add(email.Trim()))
                throw new OperacionRechazadaException("Ya existe un usuario con ese correo.");
        }
    }

    public string? NombreDe(int operadorId)
    {
        lock (candado)
            return operadores.FirstOrDefault(o => o.Id == operadorId)?.Nombre;
    }
}
