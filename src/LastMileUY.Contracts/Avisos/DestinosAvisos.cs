using System.Net;
using System.Net.Sockets;

namespace LastMileUY.Contracts.Avisos;

// Encabezados HTTP con los que el worker manda cada aviso al comercio (ver docs/mensajeria.md)
public static class EncabezadosAvisos
{
    // Identificador único del aviso: el comercio lo usa para descartar repetidos
    public const string AvisoId = "X-Aviso-Id";

    // Tipo de evento (envio.admitido, envio.estado_cambiado)
    public const string Tipo = "X-Aviso-Tipo";

    // Número de intento (1 la primera vez)
    public const string Intento = "X-Aviso-Intento";
}

// Qué URLs puede tener una suscripción a avisos. Las carga el comercio y las llama el worker desde
// adentro de nuestra red: sin estas reglas, alguien podría apuntarlas a servicios internos (SSRF).
// - Solo https, sin usuario ni contraseña en la URL.
// - Solo direcciones públicas: ni localhost, ni redes privadas, ni la de metadatos de la nube.
// El worker vuelve a revisar la dirección al conectarse (un nombre puede resolver a una IP interna).
// permitirLocales: solo para desarrollo y pruebas, con el receptor de prueba en Docker o en la máquina.
public static class DestinosAvisos
{
    // null si se puede usar; si no, el motivo
    public static string? Validar(string? url, bool permitirLocales)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
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

            return !(b[0] == 0                                  // "esta red"
                || b[0] == 10                                   // privada
                || b[0] == 100 && (b[1] & 0xC0) == 64           // 100.64.0.0/10, NAT del proveedor
                || b[0] == 127                                  // loopback
                || b[0] == 169 && b[1] == 254                   // enlace local y metadatos de la nube
                || b[0] == 172 && (b[1] & 0xF0) == 16           // privada
                || b[0] == 192 && b[1] == 0 && b[2] == 0        // reservada
                || b[0] == 192 && b[1] == 168                   // privada
                || b[0] == 198 && (b[1] & 0xFE) == 18           // pruebas de red
                || b[0] >= 224);                                // multicast y reservadas
        }

        return !(IPAddress.IsLoopback(ip)
            || ip.Equals(IPAddress.IPv6Any)
            || ip.IsIPv6LinkLocal
            || ip.IsIPv6SiteLocal
            || ip.IsIPv6UniqueLocal
            || ip.IsIPv6Multicast);
    }
}
