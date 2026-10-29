using System.Threading.RateLimiting;
using LastMileUY.PublicTracking.Components;
using LastMileUY.PublicTracking.Servicios;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();

// La página le consulta a la API desde el servidor; el navegador nunca habla con la API
var urlBase = new Uri(builder.Configuration["Api:UrlBase"]
    ?? throw new InvalidOperationException("Falta configurar Api:UrlBase."));

builder.Services.AddHttpClient<SeguimientoApi>(cliente =>
{
    cliente.BaseAddress = urlBase;
    cliente.Timeout = TimeSpan.FromSeconds(10);
});

// Tope de páginas por visitante (429 al pasarlo). La API tiene su propio tope, pero ahí todas las
// consultas de esta página llegan desde la misma IP (la de este servidor): el freno por persona va acá.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Con texto, para que no lo tape la página de "no encontrado" (UseStatusCodePagesWithReExecute)
    options.OnRejected = (contexto, cancellationToken) =>
    {
        contexto.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        return new ValueTask(contexto.HttpContext.Response.WriteAsync(
            "Hiciste muchas consultas seguidas. Probá de nuevo en un minuto.", cancellationToken));
    };

    options.AddPolicy("por-visitante", contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("Seguimiento:ConsultasPorMinuto", 20),
                Window = TimeSpan.FromMinutes(1)
            }));
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

// Encabezados de seguridad (ver docs/seguridad.md). La dirección lleva el código de seguimiento:
// no se le manda a nadie (el logo del operador puede venir de otro sitio). La página no se deja
// meter en un iframe ajeno y el navegador no adivina el tipo de contenido.
app.Use((contexto, siguiente) =>
{
    contexto.Response.Headers.XContentTypeOptions = "nosniff";
    contexto.Response.Headers["Referrer-Policy"] = "no-referrer";
    contexto.Response.Headers.XFrameOptions = "DENY";
    return siguiente(contexto);
});

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .RequireRateLimiting("por-visitante");

app.Run();
