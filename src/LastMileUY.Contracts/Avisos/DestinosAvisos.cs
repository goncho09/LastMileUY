using System.Net;
using System.Net.Sockets;

namespace LastMileUY.Contracts.Avisos;

public static class EncabezadosAvisos
{
    public const string AvisoId = "X-Aviso-Id";

    public const string Tipo = "X-Aviso-Tipo";

    public const string Intento = "X-Aviso-Intento";

    public const string Version = "X-Aviso-Version";
}

public static class DestinosAvisos
{
    public const int LargoMaximo = 2000;

    public static string? Validar(string? url, bool permitirLocales)
    {
        if (url?.Length > LargoMaximo)
            return $"La URL no puede superar los {LargoMaximo} caracteres.";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.IsFile)
            return "La URL no es válida.";

        if (uri.Scheme != Uri.UriSchemeHttps && !(permitirLocales && uri.Scheme == Uri.UriSchemeHttp))
            return "La URL tiene que ser https.";

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "La URL no puede llevar usuario ni contraseña.";

        if (permitirLocales)
            return null;

        if (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return "La URL no puede apuntar a la propia máquina.";

        if (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && !EsPublica(ip))
            return "La URL no puede apuntar a una red interna.";

        return null;
    }

    public static bool EsPublica(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();

            return !(b[0] == 0
                || b[0] == 10
                || b[0] == 100 && (b[1] & 0xC0) == 64
                || b[0] == 127
                || b[0] == 169 && b[1] == 254
                || b[0] == 172 && (b[1] & 0xF0) == 16
                || b[0] == 192 && b[1] == 0 && b[2] == 0
                || b[0] == 192 && b[1] == 168
                || b[0] == 198 && (b[1] & 0xFE) == 18
                || b[0] >= 224);
        }

        return !(IPAddress.IsLoopback(ip)
            || ip.Equals(IPAddress.IPv6Any)
            || ip.IsIPv6LinkLocal
            || ip.IsIPv6SiteLocal
            || ip.IsIPv6UniqueLocal
            || ip.IsIPv6Multicast);
    }
}
