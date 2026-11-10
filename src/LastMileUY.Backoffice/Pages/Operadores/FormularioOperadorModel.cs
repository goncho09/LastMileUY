using System.ComponentModel.DataAnnotations;
using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Pages.Operadores;

public sealed class FormularioOperadorModel
{
    [Required(ErrorMessage = "Ingresá el nombre del operador.")]
    [StringLength(150, MinimumLength = 1)]
    public string Nombre { get; set; } = "";

    [RegularExpression(
        "^#[0-9A-Fa-f]{6}$",
        ErrorMessage = "Usá un color hexadecimal, por ejemplo #2563EB.")]
    public string? ColorPrimario { get; set; }

    [StringLength(500)]
    [Url(ErrorMessage = "Ingresá una URL válida.")]
    public string? LogoUrl { get; set; }

    public GuardarOperadorAdminRequest ARequest() =>
        new(
            Nombre.Trim(),
            string.IsNullOrWhiteSpace(ColorPrimario)
                ? null : ColorPrimario.Trim(),
            string.IsNullOrWhiteSpace(LogoUrl)
                ? null : LogoUrl.Trim());

    public static FormularioOperadorModel Desde(OperadorAdminDto operador) =>
        new()
        {
            Nombre = operador.Nombre,
            ColorPrimario = operador.ColorPrimario,
            LogoUrl = operador.LogoUrl
        };
}