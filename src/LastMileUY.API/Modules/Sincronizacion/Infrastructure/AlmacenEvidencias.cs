using System.Text.RegularExpressions;

namespace LastMileUY.API.Modules.Sincronizacion.Infrastructure;

public interface IAlmacenEvidencias
{
    Task<string> GuardarAsync(int operadorId, Imagen imagen, CancellationToken ct = default);

    Task<Imagen?> LeerAsync(string clave, CancellationToken ct = default);
}

public record Imagen(byte[] Contenido, string TipoContenido)
{
    public const int TamanioMaximo = 1024 * 1024;

    public static Imagen? DesdeBase64(string base64)
    {
        byte[] contenido;

        try
        {
            contenido = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (contenido.Length is 0 or > TamanioMaximo)
        {
            return null;
        }

        if (contenido is [0x89, 0x50, 0x4E, 0x47, ..])
        {
            return new Imagen(contenido, "image/png");
        }

        if (contenido is [0xFF, 0xD8, 0xFF, ..])
        {
            return new Imagen(contenido, "image/jpeg");
        }

        return null;
    }

    public string Extension => TipoContenido == "image/png" ? "png" : "jpg";
}

public partial class AlmacenEvidenciasEnDisco(IConfiguration configuration, IHostEnvironment entorno) : IAlmacenEvidencias
{
    private readonly string directorio = configuration["Evidencias:Directorio"]
        ?? Path.Combine(entorno.ContentRootPath, "evidencias");

    [GeneratedRegex(@"^\d+/[0-9a-f]{32}\.(png|jpg)$")]
    private static partial Regex FormatoClave();

    public async Task<string> GuardarAsync(int operadorId, Imagen imagen, CancellationToken ct = default)
    {
        var clave = $"{operadorId}/{Guid.NewGuid():N}.{imagen.Extension}";
        var ruta = Path.Combine(directorio, clave);

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllBytesAsync(ruta, imagen.Contenido, ct);

        return clave;
    }

    public async Task<Imagen?> LeerAsync(string clave, CancellationToken ct = default)
    {
        if (!FormatoClave().IsMatch(clave))
        {
            return null;
        }

        var ruta = Path.Combine(directorio, clave);

        if (!File.Exists(ruta))
        {
            return null;
        }

        var tipo = clave.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg";

        return new Imagen(await File.ReadAllBytesAsync(ruta, ct), tipo);
    }
}
