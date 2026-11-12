namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LastMileUY.Compartido;
using LastMileUY.Contracts.Integraciones;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static ApiDePuntaAPunta;

// La API detrás de un proxy inverso (src/Compartido/ProxyInverso.cs), como en Azure: los pedidos llegan
// todos desde el proxy, y la IP real viene en X-Forwarded-For. Se prueba con el freno a claves inválidas,
// que es por IP: cada visitante tiene que tener su propio tope, y nadie se lo puede saltear inventándose
// el encabezado. Las IP son de documentación (RFC 5737), distintas en cada prueba.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ProxyInversoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    // El proxy de la propia máquina: confiable siempre
    private static readonly IPAddress Proxy = IPAddress.Loopback;

    [Fact]
    public async Task Detras_del_proxy_cada_visitante_tiene_su_propio_tope()
    {
        var buena = (await CrearClaveAsync()).Clave;
        var abusador = IpAlAzar("203.0.113.");
        var otro = IpAlAzar("198.51.100.");

        for (var i = 0; i < 30; i++)
            Assert.Equal(StatusCodes.Status401Unauthorized, (await PedirAsync(Proxy, "lmuy_live_inventada" + i, abusador)).Response.StatusCode);

        Assert.Equal(StatusCodes.Status429TooManyRequests, (await PedirAsync(Proxy, "lmuy_live_otra", abusador)).Response.StatusCode);

        // Por el mismo proxy, otro visitante no se entera (antes de esto, para la API eran todos "el proxy")
        Assert.Equal(StatusCodes.Status200OK, (await PedirAsync(Proxy, buena, otro)).Response.StatusCode);
    }

    [Fact]
    public async Task Desde_afuera_del_proxy_el_encabezado_inventado_no_sirve_para_saltear_el_tope()
    {
        var atacante = IpAlAzar("192.0.2.");

        // Cada pedido dice venir de una IP distinta, pero no viene del proxy: se cuenta por su IP de verdad
        for (var i = 0; i < 30; i++)
            await PedirAsync(atacante, "lmuy_live_inventada" + i, IpAlAzar("203.0.113."));

        var frenado = await PedirAsync(atacante, "lmuy_live_otra", IpAlAzar("198.51.100."));
        Assert.Equal(StatusCodes.Status429TooManyRequests, frenado.Response.StatusCode);
    }

    [Fact]
    public async Task Si_el_visitante_llego_por_https_la_API_lo_sabe_aunque_el_proxy_le_hable_por_http()
    {
        // La documentación arma la dirección del servidor con el esquema del pedido: detrás del proxy
        // tiene que decir https, que es por donde entran los integradores
        var contexto = await api.Server.SendAsync(c =>
        {
            c.Request.Method = "GET";
            c.Request.Scheme = "http";
            c.Request.Host = new HostString("api.lastmileuy.test");
            c.Request.Path = "/api/v1/openapi.json";
            c.Request.Headers["X-Forwarded-For"] = IpAlAzar("203.0.113.").ToString();
            c.Request.Headers["X-Forwarded-Proto"] = "https";
            c.Connection.RemoteIpAddress = Proxy;
        });

        using var documento = await JsonDocument.ParseAsync(contexto.Response.Body);
        var servidor = documento.RootElement.GetProperty("servers")[0].GetProperty("url").GetString();

        Assert.StartsWith("https://api.lastmileuy.test", servidor);
    }

    [Fact]
    public void Una_red_mal_escrita_en_la_configuracion_frena_el_arranque()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ProxyInverso:RedesConfiables:0"] = "172.16.0.0/99" })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AgregarProxyInverso(configuracion));
        Assert.Contains("172.16.0.0/99", error.Message);
    }

    private Task<HttpContext> PedirAsync(IPAddress desde, string clave, IPAddress ipReal) =>
        api.Server.SendAsync(contexto =>
        {
            contexto.Request.Method = "GET";
            contexto.Request.Scheme = "https";
            contexto.Request.Host = new HostString("localhost");
            contexto.Request.Path = "/api/v1/destinatarios";
            contexto.Request.Headers["X-Api-Key"] = clave;
            contexto.Request.Headers["X-Forwarded-For"] = ipReal.ToString();
            contexto.Connection.RemoteIpAddress = desde;
        });

    private async Task<ClaveApiCreadaDto> CrearClaveAsync()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        using var respuesta = await portal.PostAsJsonAsync("api/claves-api", new CrearClaveApiRequest { Nombre = "Detrás del proxy" });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ClaveApiCreadaDto>())!;
    }

    private static IPAddress IpAlAzar(string red) => IPAddress.Parse(red + Random.Shared.Next(1, 255));
}
