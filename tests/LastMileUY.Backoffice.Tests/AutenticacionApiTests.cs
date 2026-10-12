namespace LastMileUY.Backoffice.Tests;

using System.Net;
using System.Security.Claims;
using System.Text;
using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Http;

public class AutenticacionApiTests
{
    private const string UsuarioJson = """
        {
          "id": "abc", "email": "operador@prueba.test", "nombre": "Operador", "apellido": "Uno",
          "operadorId": 3, "comercioId": null, "roles": ["Operador"]
        }
        """;

    [Fact]
    public async Task Login_correcto_devuelve_el_usuario_y_la_cookie_de_la_api()
    {
        var api = new ApiFalsa(pedido => pedido.RequestUri!.AbsolutePath switch
        {
            "/api/auth/login" => ConCookie(Respuesta(HttpStatusCode.OK, "{}"),
                "LastMileUY.Auth=valor123; path=/; secure; samesite=lax; httponly"),
            "/api/auth/me" => Respuesta(HttpStatusCode.OK, UsuarioJson),
            _ => Respuesta(HttpStatusCode.NotFound, "")
        });

        var sesion = await new AutenticacionApiHttp(Cliente(api)).IniciarSesionAsync("operador@prueba.test", "clave");

        Assert.NotNull(sesion);
        Assert.Equal("Operador Uno", sesion.Nombre);
        Assert.Equal(3, sesion.OperadorId);
        Assert.Equal("LastMileUY.Auth=valor123", sesion.CookieApi);

        // El pedido a /me se hace con la cookie que devolvió el login
        var pedidoMe = api.Pedidos.Single(p => p.Ruta == "/api/auth/me");
        Assert.Equal("LastMileUY.Auth=valor123", pedidoMe.Cookie);
    }

    [Fact]
    public async Task Login_incorrecto_devuelve_null()
    {
        var api = new ApiFalsa(_ => Respuesta(HttpStatusCode.Unauthorized, ""));

        var sesion = await new AutenticacionApiHttp(Cliente(api)).IniciarSesionAsync("operador@prueba.test", "mal");

        Assert.Null(sesion);
    }

    [Fact]
    public void Si_la_cookie_viene_partida_se_juntan_todas_las_partes()
    {
        var respuesta = ConCookie(Respuesta(HttpStatusCode.OK, ""),
            "LastMileUY.Auth=chunks-2; path=/",
            "LastMileUY.AuthC1=parte1; path=/",
            "LastMileUY.AuthC2=parte2; path=/");

        Assert.Equal(
            "LastMileUY.Auth=chunks-2; LastMileUY.AuthC1=parte1; LastMileUY.AuthC2=parte2",
            AutenticacionApiHttp.LeerCookies(respuesta));
    }

    [Fact]
    public async Task Cada_llamada_a_la_api_lleva_la_cookie_del_usuario_logueado()
    {
        var api = new ApiFalsa(_ => Respuesta(HttpStatusCode.OK, "{}"));
        var cliente = ClienteConUsuario(api, "LastMileUY.Auth=del-usuario");

        await cliente.GetAsync("api/envios");

        Assert.Equal("LastMileUY.Auth=del-usuario", Assert.Single(api.Pedidos).Cookie);
    }

    [Fact]
    public async Task Si_la_api_responde_401_la_sesion_vencio()
    {
        var api = new ApiFalsa(_ => Respuesta(HttpStatusCode.Unauthorized, ""));
        var cliente = ClienteConUsuario(api, "LastMileUY.Auth=vencida");

        await Assert.ThrowsAsync<SesionVencidaException>(() => cliente.GetAsync("api/envios"));
    }

    [Theory]
    [InlineData("", "clave")]
    [InlineData("operador@prueba.test", " ")]
    public async Task Con_datos_de_prueba_hay_que_completar_email_y_contrasena(string email, string password)
    {
        Assert.Null(await new AutenticacionApiDePrueba().IniciarSesionAsync(email, password));
    }

    private static HttpClient Cliente(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://api.test/") };

    // Como en la app: el handler que reenvía la cookie, delante de la "API"
    private static HttpClient ClienteConUsuario(ApiFalsa api, string cookieApi)
    {
        var contexto = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ReenviarCookieApi.ClaimCookieApi, cookieApi)], "prueba"))
        };

        var reenviar = new ReenviarCookieApi(new HttpContextAccessor { HttpContext = contexto })
        {
            InnerHandler = api
        };

        return Cliente(reenviar);
    }

    private static HttpResponseMessage Respuesta(HttpStatusCode codigo, string cuerpo) =>
        new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ConCookie(HttpResponseMessage respuesta, params string[] cookies)
    {
        foreach (var cookie in cookies)
        {
            respuesta.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        }

        return respuesta;
    }

    // Simula la API sin red y anota qué le llegó
    private class ApiFalsa(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(string Ruta, string? Cookie)> Pedidos { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var valores) ? string.Join("; ", valores) : null;
            Pedidos.Add((request.RequestUri!.AbsolutePath, cookie));

            return Task.FromResult(responder(request));
        }
    }
}
