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

builder.Services.AddSingleton<SeleccionOperadorCookie>();
builder.Services.AddDataProtection();

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
