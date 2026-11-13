using System.Globalization;
using Microsoft.AspNetCore.Localization;
using LastMileUY.Compartido;
using System.Threading.RateLimiting;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Backoffice.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(opciones =>
{
    opciones.Conventions.AuthorizeFolder("/", Permisos.PoliticaBackoffice);
    opciones.Conventions.AuthorizeFolder("/Operador", Permisos.PoliticaConfiguracion);
    opciones.Conventions.AuthorizeFolder("/Configuracion", Permisos.PoliticaConfiguracion);
    opciones.Conventions.AuthorizeFolder("/Tablero", Permisos.PoliticaConfiguracion);
    opciones.Conventions.AuthorizeFolder(
        "/Usuarios", Permisos.PoliticaConfiguracion);

    opciones.Conventions.AllowAnonymousToPage("/Login");
    opciones.Conventions.AllowAnonymousToPage("/Logout");
    opciones.Conventions.AllowAnonymousToPage("/AccesoDenegado");
    opciones.Conventions.AllowAnonymousToPage("/Error");

    opciones.Conventions.AuthorizeFolder(
        "/Envios", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/Rutas", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/Sincronizacion", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/MensajesFallidos", Permisos.PoliticaAdministracionGlobal);

    opciones.Conventions.AuthorizeFolder(
        "/Funcionalidades", Permisos.PoliticaAdministracionGlobal);

    opciones.Conventions.AuthorizeFolder(
        "/Operadores", Permisos.PoliticaAdministracionGlobal);

    opciones.Conventions.AuthorizeFolder(
        "/Comercios", Permisos.PoliticaAdministracionGlobal);
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Permisos.PoliticaBackoffice,
        politica => politica.RequireRole(Permisos.RolesBackoffice))
    .AddPolicy(Permisos.PoliticaConfiguracion,
        politica => politica.RequireRole(Permisos.RolesConfiguracion))
    .AddPolicy(Permisos.PoliticaAdministracionGlobal,
        politica => politica.RequireRole(Permisos.RolesAdministracionGlobal))
    .AddPolicy(Permisos.PoliticaOperacion,
        politica => politica.RequireRole(Permisos.RolesOperacion));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "LastMileUY.Backoffice";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.LoginPath = "/login";
        opciones.AccessDeniedPath = "/acceso-denegado";
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = false;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();

if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("RabbitMQ")))
{
    builder.Services.AddHostedService<ConsumidorEventosEnvio>();
}

var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.Backoffice")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

var api = builder.Configuration.GetSection("Api");

if (api.GetValue<bool>("UsarDatosDePrueba"))
{
    builder.Services.AddSingleton<IEnviosApi, EnviosApiDePrueba>();
    builder.Services.AddSingleton<IAutenticacionApi, AutenticacionApiDePrueba>();
    builder.Services.AddSingleton<IOperadorApi, OperadorApiDePrueba>();
    builder.Services.AddSingleton<ICacheApi, CacheApiDePrueba>();
    builder.Services.AddSingleton<IRutasApi, RutasApiDePrueba>();
    builder.Services.AddSingleton<ISincronizacionApi, SincronizacionApiDePrueba>();
    builder.Services.AddSingleton<IMensajesFallidosApi, MensajesFallidosApiDePrueba>();
    builder.Services.AddSingleton<IFuncionalidadesApi, FuncionalidadesApiDePrueba>();
    builder.Services.AddSingleton<IUsuariosApi, UsuariosApiDePrueba>();
    builder.Services.AddSingleton<GestionOperadoresApiDePrueba>();
    builder.Services.AddSingleton<IGestionOperadoresApi>(s => s.GetRequiredService<GestionOperadoresApiDePrueba>());
    builder.Services.AddSingleton<IGestionComerciosApi, GestionComerciosApiDePrueba>();
    builder.Services.AddSingleton<IConfiguracionApi, ConfiguracionApiDePrueba>();

}
else
{
    var urlBase = new Uri(api["UrlBase"]
        ?? throw new InvalidOperationException("Falta configurar Api:UrlBase."));

    // Sin contenedor de cookies compartido: cada pedido lleva solo la cookie de su propio usuario.
    static HttpMessageHandler SinCookiesCompartidas() => new SocketsHttpHandler { UseCookies = false };

    builder.Services.AddTransient<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IConfiguracionApi, ConfiguracionApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IEnviosApi, EnviosApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IOperadorApi, OperadorApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<ICacheApi, CacheApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IRutasApi, RutasApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<ISincronizacionApi, SincronizacionApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IUsuariosApi, UsuariosApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IAutenticacionApi, AutenticacionApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

    builder.Services.AddHttpClient<IGestionOperadoresApi, GestionOperadoresApiHttp>(
            cliente =>
            {
                cliente.BaseAddress = urlBase;
                cliente.Timeout = TimeSpan.FromSeconds(10);
            })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IGestionComerciosApi, GestionComerciosApiHttp>(
            cliente =>
            {
                cliente.BaseAddress = urlBase;
                cliente.Timeout = TimeSpan.FromSeconds(10);
            })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IMensajesFallidosApi, MensajesFallidosApiHttp>(
            cliente =>
            {
                cliente.BaseAddress = urlBase;
                cliente.Timeout = TimeSpan.FromSeconds(10);
            })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

    builder.Services.AddHttpClient<IFuncionalidadesApi, FuncionalidadesApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas)
        .AddHttpMessageHandler<ReenviarCookieApi>();

}

builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (contexto, cancellationToken) =>
    {
        contexto.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        return new ValueTask(contexto.HttpContext.Response.WriteAsync(
            "Hiciste muchos intentos de iniciar sesión seguidos. Probá de nuevo en un minuto.", cancellationToken));
    };

    var intentosPorMinuto = builder.Configuration.GetValue("Login:IntentosPorMinuto", 10);

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        HttpMethods.IsPost(contexto.Request.Method)
        && contexto.Request.Path.Equals("/login", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = intentosPorMinuto,
                    Window = TimeSpan.FromMinutes(1)
                })
            : RateLimitPartition.GetNoLimiter("sin-tope"));
});

builder.Services.AgregarProxyInverso(builder.Configuration);

var app = builder.Build();

app.UsarProxyInverso();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use((contexto, siguiente) =>
{
    contexto.Response.Headers.XContentTypeOptions = "nosniff";
    contexto.Response.Headers["Referrer-Policy"] = "same-origin";
    return siguiente(contexto);
});

app.UseRateLimiter();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
    SupportedCultures = [CultureInfo.InvariantCulture],
    SupportedUICultures = [CultureInfo.InvariantCulture]
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (contexto, siguiente) =>
{
    try
    {
        await siguiente(contexto);
    }
    catch (SesionVencidaException) when (!contexto.Response.HasStarted)
    {
        await contexto.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var volverA = Uri.EscapeDataString(contexto.Request.Path + contexto.Request.QueryString);
        contexto.Response.Redirect($"/login?ReturnUrl={volverA}");
    }
});

app.MapStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

app.MapHub<EnviosHub>("/hubs/envios");

app.Run();

public partial class Program;
