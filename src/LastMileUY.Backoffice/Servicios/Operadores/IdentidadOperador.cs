using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace LastMileUY.Backoffice.Servicios;

// Cómo se ve el Backoffice para el operador logueado: nombre, color y logo.
// Se guarda en la sesión al iniciarla (claims) para no pedírselo a la API en cada página.
public partial record IdentidadOperador(string Nombre, string Color, string ColorTexto, string? LogoUrl)
{
    public const string ClaimNombre = "operador_nombre";
    public const string ClaimColor = "operador_color";
    public const string ClaimLogo = "operador_logo";

    // Gris oscuro de Bootstrap: el Backoffice de siempre si el operador no eligió color
    public const string ColorPorDefecto = "#212529";

    public string Inicial => Nombre.Length == 0 ? "?" : Nombre[..1].ToUpper(CultureInfo.CurrentCulture);

    public static IdentidadOperador? Desde(ClaimsPrincipal usuario)
    {
        var nombre = usuario.FindFirstValue(ClaimNombre);

        if (string.IsNullOrWhiteSpace(nombre))
        {
            return null;
        }

        var color = ColorSeguro(usuario.FindFirstValue(ClaimColor));

        return new IdentidadOperador(nombre, color, TextoSobre(color), LogoSeguro(usuario.FindFirstValue(ClaimLogo)));
    }

    // El color va directo al CSS: solo se acepta #RRGGBB (la base ya lo exige, esto es por las dudas)
    public static string ColorSeguro(string? color) =>
        color is not null && ColorHex().IsMatch(color) ? color : ColorPorDefecto;

    // Solo imágenes por http(s); cualquier otra cosa (javascript:, data:...) se ignora
    public static string? LogoSeguro(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    // Texto blanco sobre colores oscuros y oscuro sobre colores claros (luminancia relativa, WCAG)
    public static string TextoSobre(string color)
    {
        static double Canal(string hex)
        {
            var valor = int.Parse(hex, NumberStyles.HexNumber) / 255.0;
            return valor <= 0.03928 ? valor / 12.92 : Math.Pow((valor + 0.055) / 1.055, 2.4);
        }

        var luminancia = 0.2126 * Canal(color[1..3]) + 0.7152 * Canal(color[3..5]) + 0.0722 * Canal(color[5..7]);

        return luminancia > 0.179 ? "#212529" : "#ffffff";
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorHex();
}
