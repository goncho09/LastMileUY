using LastMileUY.Contracts.Usuarios;

namespace LastMileUY.Backoffice.Servicios;

// El personal del operador en memoria, para trabajar la pantalla sin la API (Api:UsarDatosDePrueba = true).
// Rechaza lo mismo que la API: un correo repetido o un perfil que el operador no puede crear
public class UsuariosApiDePrueba : IUsuariosApi
{
    private static readonly string[] RolesGestionables = ["Despachador", "OperarioDeposito", "Repartidor"];

    private readonly List<UsuarioDto> usuarios =
    [
        Usuario("Ana", "Pereira", "despachador@prueba.test", "Despachador"),
        Usuario("Bruno", "Silva", "deposito@prueba.test", "OperarioDeposito"),
        Usuario("Carla", "Méndez", "repartidor@prueba.test", "Repartidor")
    ];

    private readonly Lock candado = new();

    public Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct = default)
    {
        lock (candado)
        {
            IReadOnlyList<UsuarioDto> copia = usuarios.OrderBy(u => u.Apellido).ToList();
            return Task.FromResult(copia);
        }
    }

    public Task<UsuarioDto> CrearAsync(CrearUsuarioRequest request, CancellationToken ct = default)
    {
        if (!RolesGestionables.Contains(request.Rol, StringComparer.Ordinal))
            throw new OperacionRechazadaException("Rol no permitido.");

        var email = request.Email.Trim().ToLowerInvariant();

        lock (candado)
        {
            if (usuarios.Any(u => u.Email == email))
                throw new OperacionRechazadaException("Ya existe un usuario con ese correo.");

            var nuevo = Usuario(request.Nombre.Trim(), request.Apellido.Trim(), email, request.Rol);
            usuarios.Add(nuevo);
            return Task.FromResult(nuevo);
        }
    }

    public Task CambiarEstadoAsync(string usuarioId, bool activo, CancellationToken ct = default)
    {
        lock (candado)
        {
            var indice = usuarios.FindIndex(u => u.Id == usuarioId);

            if (indice < 0)
                throw new OperacionRechazadaException("No se puede modificar este usuario.");

            usuarios[indice] = usuarios[indice] with { Activo = activo };
        }

        return Task.CompletedTask;
    }

    private static UsuarioDto Usuario(string nombre, string apellido, string email, string rol) =>
        new(Guid.NewGuid().ToString(), email, nombre, apellido, rol, 1, null, true, DateTime.UtcNow);
}
