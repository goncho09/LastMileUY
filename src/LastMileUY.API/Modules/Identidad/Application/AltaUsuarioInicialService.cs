using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.Contracts.Administracion;
using Microsoft.AspNetCore.Identity;

namespace LastMileUY.API.Modules.Identidad.Application;

public sealed class AltaUsuarioInicialService(UserManager<Usuario> usuarios)
{
    public async Task CrearAsync(UsuarioInicialRequest? datos, string rol,
        int? operadorId, int? comercioId)
    {
        if (datos is null || string.IsNullOrWhiteSpace(datos.Nombre) ||
            string.IsNullOrWhiteSpace(datos.Apellido) ||
            string.IsNullOrWhiteSpace(datos.Email) ||
            string.IsNullOrWhiteSpace(datos.Password) ||
            datos.Nombre.Trim().Length > 150 ||
            datos.Apellido.Trim().Length > 150 ||
            datos.Email.Trim().Length > 250)
            throw new ArgumentException("Completá nombre, apellido, email y contraseña del responsable.");

        var email = datos.Email.Trim().ToLowerInvariant();
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new ArgumentException("El email de acceso no es válido.");

        if (await usuarios.FindByEmailAsync(email) is not null)
            throw new ArgumentException("Ya existe un usuario con ese correo.");

        var usuario = new Usuario
        {
            UserName = email,
            Email = email,
            Nombre = datos.Nombre.Trim(),
            Apellido = datos.Apellido.Trim(),
            OperadorId = operadorId,
            ComercioId = comercioId,
            Activo = true,
            EmailConfirmed = false
        };
        var resultado = await usuarios.CreateAsync(usuario, datos.Password);
        if (!resultado.Succeeded)
            throw new ArgumentException(string.Join("; ", resultado.Errors.Select(e => e.Description)));

        resultado = await usuarios.AddToRoleAsync(usuario, rol);
        if (!resultado.Succeeded)
            throw new ArgumentException(string.Join("; ", resultado.Errors.Select(e => e.Description)));
    }
}
