using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LastMileUY.Compartido;

public static class ProxyInverso
{
    public static IServiceCollection AgregarProxyInverso(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var redes = configuracion.GetSection("ProxyInverso:RedesConfiables").Get<string[]>() ?? [];
        var proxies = configuracion.GetSection("ProxyInverso:ProxiesConfiables").Get<string[]>() ?? [];

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

            // Solo el salto del proxy propio: un X-Forwarded-For armado por el cliente no se cree.
            opciones.ForwardLimit = 1;

            foreach (var red in redesValidas)
                opciones.KnownIPNetworks.Add(red);

            foreach (var ip in proxiesValidos)
                opciones.KnownProxies.Add(ip);
        });
    }

    public static IApplicationBuilder UsarProxyInverso(this IApplicationBuilder app) =>
        app.UseForwardedHeaders();
}
