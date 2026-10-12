using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Todo el Backoffice pide sesión, salvo el login y la página de error
builder.Services.AddRazorPages(opciones =>
{
    opciones.Conventions.AuthorizeFolder("/");
    opciones.Conventions.AllowAnonymousToPage("/Login");
    opciones.Conventions.AllowAnonymousToPage("/Error");
});

// Sesión propia del Backoffice (cookie cifrada). Adentro va la cookie de la API,
// que se reenvía en cada llamada (ver ReenviarCookieApi).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "LastMileUY.Backoffice";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.LoginPath = "/Login";
        // Igual que la sesión de la API: 8 horas. Si la API la corta antes, se vuelve al login.
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = false;
    });

builder.Services.AddHttpContextAccessor();

// De dónde salen los envíos: datos de prueba en memoria o la API real por HTTP.
var api = builder.Configuration.GetSection("Api");

if (api.GetValue<bool>("UsarDatosDePrueba"))
{
    builder.Services.AddSingleton<IEnviosApi, EnviosApiDePrueba>();
    builder.Services.AddSingleton<IAutenticacionApi, AutenticacionApiDePrueba>();
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

    builder.Services.AddHttpClient<IAutenticacionApi, AutenticacionApiHttp>(cliente =>
        {
            cliente.BaseAddress = urlBase;
            cliente.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(SinCookiesCompartidas);
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

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
        contexto.Response.Redirect($"/Login?ReturnUrl={volverA}");
    }
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
