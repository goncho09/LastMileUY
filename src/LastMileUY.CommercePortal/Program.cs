using System.Threading.RateLimiting;
using LastMileUY.CommercePortal.Components;
using LastMileUY.CommercePortal.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Sesión propia del portal (cookie cifrada). Adentro va la cookie de la API, que se reenvía
// en cada llamada (ver ClienteApiBase). Mismo esquema que el Backoffice.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "LastMileUY.Portal";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.LoginPath = "/login";
        // Igual que la sesión de la API: 8 horas. Si la API la corta antes, se vuelve al login.
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = false;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Claves con las que se cifra la cookie de sesión. Si se guardan en una carpeta
// (en Docker, un volumen), las sesiones sobreviven a reiniciar o recrear el contenedor.
var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.CommercePortal")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

// Clientes de la API
var urlBase = new Uri(builder.Configuration["Api:UrlBase"]
    ?? throw new InvalidOperationException("Falta configurar Api:UrlBase."));

// UseCookies = false: la cookie de cada usuario se pone a mano en cada pedido.
// Si no, el HttpClient compartido guardaría la cookie de un usuario y se la mandaría a otro.
static HttpMessageHandler SinCookiesCompartidas() => new SocketsHttpHandler { UseCookies = false };

void ConfigurarCliente(HttpClient cliente)
{
    cliente.BaseAddress = urlBase;
    cliente.Timeout = TimeSpan.FromSeconds(10);
}

builder.Services.AddScoped<SesionActual>();

builder.Services.AddHttpClient<IAutenticacionApi, AutenticacionApiHttp>(ConfigurarCliente)
    .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

builder.Services.AddHttpClient<IDestinatariosApi, DestinatariosApiHttp>(ConfigurarCliente)
    .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

builder.Services.AddHttpClient<IEnviosApi, EnviosApiHttp>(ConfigurarCliente)
    .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

builder.Services.AddHttpClient<IAvisosApi, AvisosApiHttp>(ConfigurarCliente)
    .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

builder.Services.AddSingleton<SeleccionOperadorCookie>();
builder.Services.AddDataProtection();

// Tope de intentos de inicio de sesión por visitante (429 al pasarlo). La API bloquea la cuenta después
// de 5 contraseñas mal, pero eso no frena probar una contraseña contra muchas cuentas. Y a la API todos
// los intentos le llegan desde la IP de este servidor: el freno por persona va acá (ver docs/seguridad.md).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Con texto, para que no lo tape la página de "no encontrado" (UseStatusCodePagesWithReExecute)
    options.OnRejected = (contexto, cancellationToken) =>
    {
        contexto.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        return new ValueTask(contexto.HttpContext.Response.WriteAsync(
            "Hiciste muchos intentos de iniciar sesión seguidos. Probá de nuevo en un minuto.", cancellationToken));
    };

    var intentosPorMinuto = builder.Configuration.GetValue("Login:IntentosPorMinuto", 10);

    // Solo cuenta los envíos del login y de la elección de operador (que también inicia la sesión)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        HttpMethods.IsPost(contexto.Request.Method)
        && (contexto.Request.Path.Equals("/login", StringComparison.OrdinalIgnoreCase)
            || contexto.Request.Path.Equals("/seleccionar-operador", StringComparison.OrdinalIgnoreCase))
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
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
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

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

// Cerrar sesión: avisa a la API (si se puede) y borra la cookie del portal
app.MapPost("/salir", async (HttpContext contexto, IAutenticacionApi autenticacion) =>
{
    var cookieApi = contexto.User.FindFirst(ClaimsPortal.CookieApi)?.Value;

    if (cookieApi is not null)
    {
        try
        {
            await autenticacion.CerrarSesionAsync(cookieApi);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Si la API no responde igual se cierra la sesión del portal
        }
    }

    await contexto.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("~/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
