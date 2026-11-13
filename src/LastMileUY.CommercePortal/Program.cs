using LastMileUY.Compartido;
using System.Threading.RateLimiting;
using LastMileUY.CommercePortal.Components;
using LastMileUY.CommercePortal.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "LastMileUY.Portal";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.LoginPath = "/login";
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = false;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.CommercePortal")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

var urlBase = new Uri(builder.Configuration["Api:UrlBase"]
    ?? throw new InvalidOperationException("Falta configurar Api:UrlBase."));

// Sin contenedor de cookies compartido: cada pedido lleva solo la cookie de su propio usuario.
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

builder.Services.AddHttpClient<IClavesApi, ClavesApiHttp>(ConfigurarCliente)
    .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

builder.Services.AddHttpClient<IAvisosApi, AvisosApiHttp>(ConfigurarCliente)
    .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);

builder.Services.AddSingleton<SeleccionOperadorCookie>();
builder.Services.AddDataProtection();

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

builder.Services.AgregarProxyInverso(builder.Configuration);

var app = builder.Build();

app.UsarProxyInverso();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

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
        }
    }

    await contexto.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("~/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
