using LastMileUY.Compartido;
using System.Threading.RateLimiting;
using LastMileUY.PublicTracking.Components;
using LastMileUY.PublicTracking.Servicios;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

var urlBase = new Uri(builder.Configuration["Api:UrlBase"]
    ?? throw new InvalidOperationException("Falta configurar Api:UrlBase."));

builder.Services.AddHttpClient<SeguimientoApi>(cliente =>
{
    cliente.BaseAddress = urlBase;
    cliente.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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
