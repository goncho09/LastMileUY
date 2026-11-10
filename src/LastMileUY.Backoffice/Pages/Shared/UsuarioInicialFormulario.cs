using System.ComponentModel.DataAnnotations;
using LastMileUY.Contracts.Administracion;

namespace LastMileUY.Backoffice.Pages.Shared;

public sealed class UsuarioInicialFormulario
{
    [Required, StringLength(150)]
    public string Nombre { get; set; } = "";
    [Required, StringLength(150)]
    public string Apellido { get; set; } = "";
    [Required, EmailAddress, StringLength(250)]
    public string Email { get; set; } = "";
    [Required, MinLength(8)]
    public string Password { get; set; } = "";

    public UsuarioInicialRequest ARequest() => new(
        Nombre.Trim(), Apellido.Trim(), Email.Trim(), Password);
}
