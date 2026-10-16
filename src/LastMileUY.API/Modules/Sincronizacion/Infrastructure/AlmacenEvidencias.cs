using System.Text.RegularExpressions;

namespace LastMileUY.API.Modules.Sincronizacion.Infrastructure;

// Dónde quedan las imágenes de evidencia (firma y foto) que manda la app del repartidor.
// En la base solo se guarda la clave (PruebaEntrega.FirmaArchivoKey / FotoArchivoKey).
public interface IAlmacenEvidencias
{
    // Devuelve la clave con la que se guardó
    Task<string> GuardarAsync(int operadorId, Imagen imagen, CancellationToken ct = default);

    // null si no existe
    Task<Imagen?> LeerAsync(string clave, CancellationToken ct = default);
}

public record Imagen(byte[] Contenido, string TipoContenido)
{
    public const int TamanioMaximo = 1024 * 1024;

    // Acepta solo PNG y JPEG, reconocidos por sus primeros bytes (no por lo que diga la app).
    // null si el texto no es base64, está vacío, es muy grande o no es una imagen de esas.
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

// Guarda cada imagen como archivo en una carpeta ("Evidencias:Directorio"; en Docker, un volumen).
// La clave es "{operador}/{guid}.{extensión}": la arma la API, nunca la app, así que no se puede
// usar para leer otro archivo del servidor.
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
