using System.ComponentModel.DataAnnotations;
using LastMileUY.Contracts.Comercios;

namespace LastMileUY.Backoffice.Pages.Comercios;

public sealed class FormularioComercioModel
{
    [Required(ErrorMessage = "Ingresá el nombre.")]
    [StringLength(150)]
    public string Nombre { get; set; } = "";

    [StringLength(200)]
    public string? RazonSocial { get; set; }

    [StringLength(20)]
    public string? RUT { get; set; }

    [EmailAddress(ErrorMessage = "El email no es válido.")]
    [StringLength(250)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Telefono { get; set; }

    public GuardarComercioAdminRequest ARequest() =>
        new(Nombre.Trim(), Limpiar(RazonSocial), Limpiar(RUT),
            Limpiar(Email), Limpiar(Telefono));

    public static FormularioComercioModel Desde(ComercioAdminDto c) =>
        new()
        {
            Nombre = c.Nombre,
            RazonSocial = c.RazonSocial,
            RUT = c.RUT,
            Email = c.Email,
            Telefono = c.Telefono
        };

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}