using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Usuarios;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Identidad.Application;

public sealed class GestionUsuariosService(
    UserManager<Usuario> userManager,
    ApplicationDbContext db,
    IContextoOperador contextoOperador,
    IHttpContextAccessor httpContextAccessor)
{
    private static readonly string[] RolesGestionables =
    [
        "Despachador",
        "OperarioDeposito",
        "Repartidor"
    ];

    private async Task<int?> ObtenerOperadorAutorizadoAsync()
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true ||
            !principal.IsInRole("Operador"))
        {
            return null;
        }

        var operadorId = contextoOperador.OperadorId;

        if (operadorId is null ||
            contextoOperador.ComercioId is not null)
        {
            return null;
        }

        var usuario = await userManager.GetUserAsync(principal);

        if (usuario is null ||
            !usuario.Activo ||
            usuario.OperadorId != operadorId ||
            usuario.ComercioId is not null ||
            !await userManager.IsInRoleAsync(usuario, "Operador"))
        {
            return null;
        }

        return operadorId;
    }

    public async Task<List<UsuarioDto>> ListarAsync(
        CancellationToken cancellationToken = default)
    {
        var operadorId = await ObtenerOperadorAutorizadoAsync();

        if (operadorId is null)
            throw new UnauthorizedAccessException(
                "No autorizado para gestionar usuarios.");

        var usuarios = await userManager.Users
            .AsNoTracking()
            .Where(u =>
                u.OperadorId == operadorId &&
                u.ComercioId == null)
            .OrderBy(u => u.Nombre)
            .ThenBy(u => u.Apellido)
            .ToListAsync(cancellationToken);

        var resultado = new List<UsuarioDto>();

        foreach (var usuario in usuarios)
        {
            var roles = await userManager.GetRolesAsync(usuario);

            var rolesVisibles = roles
                .Where(r => RolesGestionables.Contains(
                    r, StringComparer.Ordinal))
                .ToArray();

            if (rolesVisibles.Length == 0 ||
                roles.Any(r =>
                    !RolesGestionables.Contains(
                        r, StringComparer.Ordinal)))
            {
                continue;
            }

            resultado.Add(new UsuarioDto(
                usuario.Id,
                usuario.Email ?? string.Empty,
                usuario.Nombre,
                usuario.Apellido,
                string.Join(", ", rolesVisibles),
                usuario.OperadorId,
                usuario.ComercioId,
                usuario.Activo,
                usuario.FechaCreacion
            ));
        }

        return resultado;
    }

    public async Task<(UsuarioDto? Usuario, string? Error)> CrearAsync(
        CrearUsuarioRequest request,
        CancellationToken cancellationToken = default)
    {
        var operadorId = await ObtenerOperadorAutorizadoAsync();

        if (operadorId is null)
            throw new UnauthorizedAccessException(
                "No autorizado para crear usuarios.");

        var email = request.Email?.Trim().ToLowerInvariant();
        var nombre = request.Nombre?.Trim();
        var apellido = request.Apellido?.Trim();
        var rol = request.Rol?.Trim();

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(nombre) ||
            string.IsNullOrWhiteSpace(apellido) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return (null, "Faltan datos obligatorios.");
        }

        if (rol is null ||
            !RolesGestionables.Contains(
                rol, StringComparer.Ordinal))
        {
            return (null, "Rol no permitido.");
        }

        if (request.OperadorId is not null ||
            request.ComercioId is not null)
        {
            return (null,
                "No se permite indicar operador o comercio. " +
                "Se utiliza el operador de la sesión.");
        }

        var operadorActivo = await db.Operadores
            .AsNoTracking()
            .AnyAsync(
                o => o.Id == operadorId.Value && o.Activo,
                cancellationToken);

        if (!operadorActivo)
            return (null, "El operador está inactivo o no existe.");

        if (await userManager.FindByEmailAsync(email) is not null)
            return (null, "Ya existe un usuario con ese correo.");

        await using var transaccion =
            await db.Database.BeginTransactionAsync(
                cancellationToken);

        var usuario = new Usuario
        {
            UserName = email,
            Email = email,
            Nombre = nombre!,
            Apellido = apellido!,
            OperadorId = operadorId.Value,
            ComercioId = null,
            Activo = true,
            EmailConfirmed = false
        };

        var creacion = await userManager.CreateAsync(
            usuario, request.Password!);

        if (!creacion.Succeeded)
        {
            return (null, string.Join("; ",
                creacion.Errors.Select(e => e.Description)));
        }

        var asignacion = await userManager.AddToRoleAsync(
            usuario, rol);

        if (!asignacion.Succeeded)
        {
            return (null, string.Join("; ",
                asignacion.Errors.Select(e => e.Description)));
        }

        if (rol == "Repartidor")
        {
            db.Repartidores.Add(new Repartidor
            {
                UsuarioId = usuario.Id,
                OperadorId = operadorId.Value,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaccion.CommitAsync(cancellationToken);

        return (new UsuarioDto(
            usuario.Id,
            usuario.Email ?? string.Empty,
            usuario.Nombre,
            usuario.Apellido,
            rol,
            usuario.OperadorId,
            usuario.ComercioId,
            usuario.Activo,
            usuario.FechaCreacion
        ), null);
    }


    public async Task<(bool Exito, string? Error)> CambiarEstadoAsync(
        string usuarioId,
        bool activo,
        CancellationToken cancellationToken = default)
    {
        var operadorId = await ObtenerOperadorAutorizadoAsync();

        if (operadorId is null)
            throw new UnauthorizedAccessException(
                "No autorizado para gestionar usuarios.");

        var usuario = await userManager.Users
            .FirstOrDefaultAsync(u =>
                    u.Id == usuarioId &&
                    u.OperadorId == operadorId.Value &&
                    u.ComercioId == null,
                cancellationToken);

        if (usuario is null)
            return (false, "Usuario no encontrado.");

        var roles = await userManager.GetRolesAsync(usuario);

        if (roles.Count != 1 ||
            !RolesGestionables.Contains(roles[0], StringComparer.Ordinal))
        {
            return (false, "No se puede modificar este usuario.");
        }

        if (usuario.Activo == activo)
            return (true, null);

        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        Repartidor? repartidor = null;

        if (roles[0] == "Repartidor")
        {
            var repartidorId = await db.Repartidores
                .Where(r =>
                    r.UsuarioId == usuario.Id &&
                    r.OperadorId == operadorId.Value)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (repartidorId is null)
                return (false, "No se encontró la ficha del repartidor.");

            repartidor = await BloqueoRepartidor.ObtenerAsync(
                db,
                repartidorId.Value,
                cancellationToken);

            if (repartidor is null ||
                repartidor.OperadorId != operadorId.Value)
            {
                return (false, "No se encontró la ficha del repartidor.");
            }

            if (!activo)
            {
                var tieneRutasAbiertas = await db.Rutas
                    .AnyAsync(r =>
                            r.RepartidorId == repartidor.Id &&
                            r.OperadorId == operadorId.Value &&
                            Ruta.EstadosAbiertos.Contains(r.Estado),
                        cancellationToken);

                if (tieneRutasAbiertas)
                {
                    return (false,
                        "No se puede desactivar el repartidor porque tiene hojas de ruta abiertas. " +
                        "Finalizá o reasigná sus rutas antes de desactivarlo.");
                }
            }
        }

        usuario.Activo = activo;

        var resultado = await userManager.UpdateAsync(usuario);

        if (!resultado.Succeeded)
        {
            await transaccion.RollbackAsync(cancellationToken);

            return (false, string.Join("; ",
                resultado.Errors.Select(e => e.Description)));
        }

        var resultadoStamp = await userManager.UpdateSecurityStampAsync(usuario);

        if (!resultadoStamp.Succeeded)
        {
            await transaccion.RollbackAsync(cancellationToken);

            return (false, "No se pudo invalidar la sesión del usuario.");
        }

        if (repartidor is not null)
        {
            await db.Repartidores
                .Where(r =>
                    r.Id == repartidor.Id &&
                    r.OperadorId == operadorId.Value)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(r => r.Activo, activo),
                    cancellationToken);
        }

        await transaccion.CommitAsync(cancellationToken);

        return (true, null);
    }

}
