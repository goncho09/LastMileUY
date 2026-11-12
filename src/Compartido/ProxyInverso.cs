using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LastMileUY.Compartido;

// La IP real del visitante detrás de un proxy inverso (docs/seguridad.md, "Detrás de un proxy").
// En Azure cada aplicación escucha solo dentro de la máquina y un proxy le pasa los pedidos de internet:
// sin esto, todos los pedidos parecen venir de la IP del proxy, y los topes por IP (claves de API
// inválidas, login, seguimiento público) serían uno solo para todos. Con un visitante abusando, se
// frenaría a todos los demás.
// El proxy manda la IP original en X-Forwarded-For (y si el pedido llegó por https, X-Forwarded-Proto).
// Ese encabezado se cree SOLO si el pedido viene de un proxy confiable: si no, cualquiera podría mandar
// uno inventado y saltearse los topes. Confiables: la propia máquina (siempre) y las redes o IPs que diga
// la configuración (ProxyInverso:RedesConfiables, ProxyInverso:ProxiesConfiables).
// Este archivo lo comparten la API, el Backoffice, el portal y el seguimiento público (está enlazado
// en cada .csproj): las cuatro van detrás del mismo proxy.
public static class ProxyInverso
{
    public static IServiceCollection AgregarProxyInverso(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var redes = configuracion.GetSection("ProxyInverso:RedesConfiables").Get<string[]>() ?? [];
        var proxies = configuracion.GetSection("ProxyInverso:ProxiesConfiables").Get<string[]>() ?? [];

        // Que una dirección mal escrita frene el arranque, y no que se ignore en silencio
        var redesValidas = redes.Select(r => System.Net.IPNetwork.TryParse(r, out var red)
            ? red
            : throw new InvalidOperationException($"ProxyInverso:RedesConfiables: '{r}' no es una red válida (por ejemplo 172.16.0.0/12)."))
            .ToList();
        var proxiesValidos = proxies.Select(p => IPAddress.TryParse(p, out var ip)
            ? ip
            : throw new InvalidOperationException($"ProxyInverso:ProxiesConfiables: '{p}' no es una IP válida."))
            .ToList();

        return servicios.Configure<ForwardedHeadersOptions>(opciones =>
        {
            opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Un solo proxy: se toma la última IP que agregó, que es la que él vio. Las anteriores las
            // pudo haber escrito el visitante
            opciones.ForwardLimit = 1;

            // Por defecto ya confía en la propia máquina (127.0.0.0/8 y ::1); se suman las configuradas
            foreach (var red in redesValidas)
                opciones.KnownIPNetworks.Add(red);

            foreach (var ip in proxiesValidos)
                opciones.KnownProxies.Add(ip);
        });
    }

    // Va primero en el pipeline: todo lo que viene después (topes, logs, redirecciones) ve la IP real
    public static IApplicationBuilder UsarProxyInverso(this IApplicationBuilder app) =>
        app.UseForwardedHeaders();
}
