using System.Threading.RateLimiting;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Backoffice.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Todo el Backoffice pide sesión con un perfil del Backoffice, salvo el login, la salida y las páginas de error.
// La configuración del operador además pide el perfil Operador (ver Permisos).
builder.Services.AddRazorPages(opciones =>
{
    // Permisos
    opciones.Conventions.AuthorizeFolder("/", Permisos.PoliticaBackoffice);
    opciones.Conventions.AuthorizeFolder("/Operador", Permisos.PoliticaConfiguracion);
    // Tablero técnico (métricas del caché): mismo perfil que la configuración del operador
    opciones.Conventions.AuthorizeFolder("/Tablero", Permisos.PoliticaConfiguracion);

    // Páginas públicas
    opciones.Conventions.AllowAnonymousToPage("/Login");
    opciones.Conventions.AllowAnonymousToPage("/Logout");
    opciones.Conventions.AllowAnonymousToPage("/AccesoDenegado");
    opciones.Conventions.AllowAnonymousToPage("/Error");
    
    opciones.Conventions.AuthorizePage(
        "/Index", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/Envios", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/Rutas", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/Sincronizacion", Permisos.PoliticaOperacion);

    opciones.Conventions.AuthorizeFolder(
        "/MensajesFallidos", Permisos.PoliticaAdministracionGlobal);
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

// Sesión propia del Backoffice (cookie cifrada). Adentro va la cookie de la API,
// que se reenvía en cada llamada (ver ReenviarCookieApi).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "LastMileUY.Backoffice";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.LoginPath = "/login";
        opciones.AccessDeniedPath = "/acceso-denegado";
        // Igual que la sesión de la API: 8 horas. Si la API la corta antes, se vuelve al login.
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = false;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddHostedService<ConsumidorEventosEnvio>();

// Claves con las que se cifra la cookie de sesión. Si se guardan en una carpeta
// (en Docker, un volumen), las sesiones sobreviven a reiniciar o recrear el contenedor.
var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.Backoffice")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

// De dónde salen los envíos y las hojas de ruta: datos de prueba en memoria o la API real por HTTP.
var api = builder.Configuration.GetSection("Api");

if (api.GetValue<bool>("UsarDatosDePrueba"))
{
    builder.Services.AddSingleton<IEnviosApi, EnviosApiDePrueba>();
    builder.Services.AddSingleton<IAutenticacionApi, AutenticacionApiDePrueba>();
    builder.Services.AddSingleton<IOperadorApi, OperadorApiDePrueba>();
    builder.Services.AddSingleton<ICacheApi, CacheApiDePrueba>();
    builder.Services.AddSingleton<IRutasApi, RutasApiDePrueba>();
    builder.Services.AddSingleton<ISincronizacionApi, SincronizacionApiDePrueba>();
    builder.Services.AddSingleton<
        IMensajesFallidosApi,
        MensajesFallidosApiDePrueba>();
}
else
{
    var urlBase = new Uri(api["UrlBase"]
        ?? throw new InvalidOperationException("Falta configurar Api:UrlBase."));

    // UseCookies = false: la cookie de cada usuario se pone a mano en cada pedido.
    // Si no, el HttpClient compartido guardaría la cookie de un usuario y se la mandaría a otro.
    static HttpMessageHandler SinCookiesCompartidas() => new SocketsHttpHandler { UseCookies = false };

    builder.Services.AddTransient<ReenviarCookieApi>();

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

    builder.Services.AddHttpClient<IAutenticacionApi, AutenticacionApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);
    
    
    builder.Services.AddHttpClient<IMensajesFallidosApi, MensajesFallidosApiHttp>(
            cliente =>
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

// Tope de intentos de inicio de sesión por visitante (429 al pasarlo). La API bloquea la cuenta después
// de 5 contraseñas mal, pero eso no frena probar una contraseña contra muchas cuentas. Y a la API todos
// los intentos le llegan desde la IP de este servidor: el freno por persona va acá (ver docs/seguridad.md).
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

    // Solo cuenta los envíos del formulario de login; el resto del Backoffice no tiene tope
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


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Encabezados de seguridad (ver docs/seguridad.md): el navegador no adivina el tipo de contenido
// y a otros sitios no les llega la dirección de la página que los enlaza
app.Use((contexto, siguiente) =>
{
    contexto.Response.Headers.XContentTypeOptions = "nosniff";
    contexto.Response.Headers["Referrer-Policy"] = "same-origin";
    return siguiente(contexto);
});

app.UseRateLimiter();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Si la API dice que la sesión venció, se cierra la del Backoffice y se vuelve al login
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
